#!/usr/bin/env python3
"""
ZPPA e-Procurement (Zambia) Live Tender Scraper
URL: https://eprocure.zppa.org.zm

Features:
- Solves session CAPTCHA on /epps/prepareCurrentOpportunities.do via Google Gemini Flash
- Queries /epps/quickSearchAction.do for active published tenders (Bid Submission)
- Paginates through current tender listings
- Scrapes full tender notice parameters from /epps/cft/prepareViewCfTWS.do?resourceId={id}
- Scrapes attached bidding documents from /epps/cft/listContractDocuments.do?resourceId={id}
- Downloads official Notice PDFs from /epps/cft/downloadNoticeForAdvSearch.do?resourceId={id}
- Downloads official Contract Documents from /epps/cft/downloadContractDocument.do?resourceId={id}&documentId={docId}
- Ingests and merges records into Supabase `tenders` table with country="ZM" and status="live"
"""

import os
import sys
import re
import json
import time
import base64
import argparse
from datetime import datetime, timezone
import urllib.parse
from curl_cffi import requests
from tender_classifier import classification_payload, classify_tender

# Determine paths and load environment variables
script_dir = os.path.dirname(os.path.abspath(__file__))
project_root = os.path.abspath(os.path.join(script_dir, ".."))
env_path = os.path.join(project_root, "next-shadcn-admin-dashboard", ".env.local")

env_vars = {}
if os.path.exists(env_path):
    with open(env_path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line and not line.startswith("#") and "=" in line:
                k, v = line.split("=", 1)
                env_vars[k.strip()] = v.strip().strip("\"'")

SUPABASE_URL = os.getenv("SUPABASE_URL") or os.getenv("NEXT_PUBLIC_SUPABASE_URL") or env_vars.get("NEXT_PUBLIC_SUPABASE_URL") or env_vars.get("SUPABASE_URL")
SUPABASE_KEY = os.getenv("SUPABASE_SECRET_KEY") or os.getenv("SUPABASE_KEY") or env_vars.get("SUPABASE_SECRET_KEY") or os.getenv("NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY") or env_vars.get("NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY")
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY") or env_vars.get("GEMINI_API_KEY")

ZPPA_BASE = "https://eprocure.zppa.org.zm"
SEARCH_PREP_URL = f"{ZPPA_BASE}/epps/prepareCurrentOpportunities.do?currentType=cft"
SEARCH_ACTION_URL = f"{ZPPA_BASE}/epps/quickSearchAction.do"
CAPTCHA_URL = f"{ZPPA_BASE}/epps/genCaptcha/captcha.jpg"
VIEW_CFT_URL = f"{ZPPA_BASE}/epps/cft/prepareViewCfTWS.do"
LIST_DOCS_URL = f"{ZPPA_BASE}/epps/cft/listContractDocuments.do"
NOTICE_PDF_URL = f"{ZPPA_BASE}/epps/cft/downloadNoticeForAdvSearch.do"
DOWNLOAD_DOC_URL = f"{ZPPA_BASE}/epps/cft/downloadContractDocument.do"

HEADERS = {
    "User-Agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
    "Accept-Language": "en-US,en;q=0.9",
}


def clean_text(text: str) -> str:
    if not text:
        return ""
    text = re.sub(r"<[^>]+>", " ", text)
    text = (
        text.replace("&amp;", "&")
        .replace("&quot;", "\"")
        .replace("&#39;", "'")
        .replace("&nbsp;", " ")
        .replace("&lt;", "<")
        .replace("&gt;", ">")
    )
    return re.sub(r"\s+", " ", text).strip()


def parse_zambia_date(date_str: str) -> str:
    """Parses date formats like '08/10/2026 10:30:00' or '08/10/2026' into ISO 8601 string."""
    if not date_str:
        return ""
    clean = clean_text(date_str)
    for fmt in [
        "%d/%m/%Y %H:%M:%S",
        "%d/%m/%Y %H:%M",
        "%d/%m/%Y",
        "%Y-%m-%d %H:%M:%S",
        "%Y-%m-%d",
    ]:
        try:
            dt = datetime.strptime(clean, fmt)
            # Zambia is CAT (UTC+2)
            return dt.strftime("%Y-%m-%dT%H:%M:%S+02:00")
        except ValueError:
            continue
    return clean


def solve_captcha_gemini(image_bytes: bytes) -> str:
    """Uses Gemini to solve the ZPPA 6-character captcha with fallback models."""
    if not GEMINI_API_KEY:
        print("Warning: GEMINI_API_KEY not found, cannot solve captcha with AI", file=sys.stderr)
        return ""

    b64_data = base64.b64encode(image_bytes).decode("utf-8")
    candidate_models = ["gemini-2.5-flash", "gemini-2.0-flash", "gemini-1.5-flash"]

    payload = {
        "contents": [{
            "parts": [
                {
                    "text": "Output ONLY the alphanumeric characters shown in this captcha image as a single lowercase word with no punctuation, markdown, or spaces. Example: a1b2c3"
                },
                {
                    "inlineData": {
                        "mimeType": "image/png",
                        "data": b64_data
                    }
                }
            ]
        }],
        "generationConfig": {
            "temperature": 0.0
        }
    }

    for model_name in candidate_models:
        url = f"https://generativelanguage.googleapis.com/v1beta/models/{model_name}:generateContent?key={GEMINI_API_KEY}"
        try:
            resp = requests.post(url, json=payload, timeout=12)
            if resp.status_code == 200:
                data = resp.json()
                candidates = data.get("candidates", [])
                if candidates:
                    parts = candidates[0].get("content", {}).get("parts", [])
                    text_parts = [p.get("text", "") for p in parts if "text" in p]
                    raw = "".join(text_parts).strip()
                    code = re.sub(r"[^a-zA-Z0-9]", "", raw).strip().lower()
                    if len(code) >= 4:
                        return code
            elif resp.status_code == 429:
                print(f"Model {model_name} quota exceeded (429), trying fallback model...", file=sys.stderr)
                time.sleep(0.5)
                continue
            else:
                print(f"Gemini model {model_name} error {resp.status_code}: {resp.text[:120]}", file=sys.stderr)
        except Exception as e:
            print(f"Error calling {model_name}: {e}", file=sys.stderr)

    return ""


class ZppaSession:
    """Manages an authorized HTTP session with ZPPA e-Procurement."""

    def __init__(self):
        self.session = requests.Session()
        self.session.headers.update(HEADERS)
        self.last_captcha = ""
        self.is_authenticated = False

    def init_and_search(self, max_retries: int = 4) -> tuple[bool, str]:
        """Initializes session, solves captcha, and submits quickSearchAction."""
        for attempt in range(1, max_retries + 1):
            print(f"Initializing ZPPA session (attempt {attempt}/{max_retries})...")
            try:
                # 1. GET search preparation page to receive cookies
                r1 = self.session.get(SEARCH_PREP_URL, timeout=15)
                if r1.status_code != 200:
                    time.sleep(1)
                    continue

                # 2. Download captcha image
                r_img = self.session.get(
                    CAPTCHA_URL,
                    headers={"Referer": SEARCH_PREP_URL},
                    timeout=12
                )
                if r_img.status_code != 200 or len(r_img.content) < 500:
                    time.sleep(1)
                    continue

                # 3. Solve captcha
                captcha_code = solve_captcha_gemini(r_img.content)
                if not captcha_code or len(captcha_code) < 4:
                    print("  Failed to extract captcha text, retrying...")
                    time.sleep(1)
                    continue

                self.last_captcha = captcha_code
                print(f"  Solved CAPTCHA: '{captcha_code}'")

                # 4. POST search action
                form_data = {
                    "mode": "search",
                    "current": "true",
                    "type": "",
                    "searchType": "cftFTS",
                    "captcha": captcha_code,
                }
                r_post = self.session.post(
                    SEARCH_ACTION_URL,
                    data=form_data,
                    headers={
                        "Referer": SEARCH_PREP_URL,
                        "Content-Type": "application/x-www-form-urlencoded",
                    },
                    timeout=20
                )

                html = r_post.text
                if "Code mismatch" in html:
                    print("  CAPTCHA mismatch response from ZPPA, retrying with fresh image...")
                    time.sleep(1)
                    continue

                if "Search Results" in html or "SearchResults" in html or "results in total" in html:
                    self.is_authenticated = True
                    print("  Successfully authenticated ZPPA search session!")
                    return True, html

            except Exception as e:
                print(f"  Session initialization error: {e}", file=sys.stderr)
                time.sleep(1.5)

        return False, ""

    def get_page(self, page_num: int) -> str:
        """Fetches page_num of search results."""
        if page_num == 1:
            url = f"{SEARCH_ACTION_URL}?mode=search&current=true&type=&searchType=cftFTS&captcha={self.last_captcha}"
        else:
            url = f"{SEARCH_ACTION_URL}?mode=search&current=true&type=&d-3680175-p={page_num}&searchType=cftFTS&captcha={self.last_captcha}"

        r = self.session.get(url, headers={"Referer": SEARCH_ACTION_URL}, timeout=20)
        if r.status_code == 200:
            return r.text
        return ""


def parse_search_results_page(html: str) -> list[dict]:
    """Extracts tenders from the search results table."""
    tenders = []
    if not html:
        return tenders

    # Find table rows inside <tbody>
    tbody_match = re.search(r"<tbody>(.*?)</tbody>", html, re.DOTALL | re.I)
    if not tbody_match:
        return tenders

    tbody = tbody_match.group(1)
    rows = re.findall(r"<tr>(.*?)</tr>", tbody, re.DOTALL | re.I)

    for row in rows:
        tds = re.findall(r"<td[^>]*>(.*?)</td>", row, re.DOTALL | re.I)
        if len(tds) < 8:
            continue

        try:
            # td[1]: Title + link
            title_match = re.search(r'<a\s+[^>]*href="([^"]*prepareViewCfTWS\.do\?resourceId=(\d+)[^"]*)"[^>]*>(.*?)</a>', tds[1], re.I)
            if not title_match:
                res_id_clean = clean_text(tds[2])
                if not res_id_clean.isdigit():
                    continue
                resource_id = res_id_clean
                title = clean_text(tds[1])
                detail_path = f"/epps/cft/prepareViewCfTWS.do?resourceId={resource_id}"
            else:
                detail_path = title_match.group(1)
                resource_id = title_match.group(2)
                title = clean_text(title_match.group(3))

            pe = clean_text(tds[3]) if len(tds) > 3 else ""

            # Description tooltip from info icon
            desc_match = re.search(r'title=[\'"]([^\'"]+)[\'"]', tds[4]) if len(tds) > 4 else None
            description = desc_match.group(1) if desc_match else title

            pub_date_raw = clean_text(tds[5]) if len(tds) > 5 else ""
            closing_date_raw = clean_text(tds[6]) if len(tds) > 6 else ""
            procedure = clean_text(tds[7]) if len(tds) > 7 else "Open Bidding National"
            status = clean_text(tds[8]) if len(tds) > 8 else "Bid Submission"

            # Notice PDF link
            pdf_url = f"{NOTICE_PDF_URL}?resourceId={resource_id}"
            detail_url = f"{VIEW_CFT_URL}?resourceId={resource_id}"

            tenders.append({
                "resource_id": resource_id,
                "title": title,
                "procuring_entity": pe,
                "description": description,
                "publish_date_raw": pub_date_raw,
                "closing_date_raw": closing_date_raw,
                "publish_date": parse_zambia_date(pub_date_raw),
                "closing_date": parse_zambia_date(closing_date_raw),
                "procedure": procedure,
                "status": status,
                "pdf_url": pdf_url,
                "details_url": detail_url,
            })
        except Exception as ex:
            print(f"Error parsing row: {ex}", file=sys.stderr)
            continue

    return tenders


def fetch_tender_detail(resource_id: str) -> dict:
    """
    Fetches full tender detail parameters and attached documents for a given resourceId.
    Does not require a session or captcha (endpoints are open).
    """
    clean_id = str(resource_id).strip()
    url = f"{VIEW_CFT_URL}?resourceId={clean_id}"

    detail_data = {
        "resourceId": clean_id,
        "officialUrl": url,
        "noticePdfUrl": f"{NOTICE_PDF_URL}?resourceId={clean_id}",
        "rawFields": {},
        "lots": [],
        "unspscCodes": [],
        "documents": [],
        "lineItems": [],
    }

    try:
        # 1. Fetch main tender view
        r = requests.get(url, headers=HEADERS, timeout=15)
        if r.status_code == 200:
            html = r.text

            # Header strong title
            header_match = re.search(r"Tender:&nbsp;<strong>(.*?)</strong>", html, re.DOTALL | re.I)
            if header_match:
                detail_data["title"] = clean_text(header_match.group(1))

            # Definition list: <dt> ... </dt> <dd> ... </dd>
            dl_matches = re.findall(r"<dt[^>]*>(.*?)</dt>\s*<dd[^>]*>(.*?)</dd>", html, re.DOTALL | re.I)
            for dt_raw, dd_raw in dl_matches:
                k = clean_text(dt_raw).rstrip(":")
                v = clean_text(dd_raw)
                if not k:
                    continue

                detail_data["rawFields"][k] = v
                kl = k.lower()

                if "procuring entity" in kl:
                    detail_data["procuringEntity"] = v
                elif "app reference number" in kl:
                    detail_data["appReferenceNumber"] = v
                elif "tender unique id" in kl:
                    detail_data["tenderUniqueId"] = v
                elif "title" in kl and "title" not in detail_data:
                    detail_data["title"] = v
                elif "description" in kl:
                    detail_data["description"] = v
                elif "procurement method rationale" in kl:
                    detail_data["procurementMethodRationale"] = v
                elif "award criteria details" in kl:
                    detail_data["awardCriteriaDetails"] = v
                elif "submission method details" in kl:
                    detail_data["submissionMethodDetails"] = v
                elif "procurement type" in kl:
                    detail_data["procurementType"] = v
                elif "procedure" in kl:
                    detail_data["procedure"] = v
                elif "commencement type" in kl:
                    detail_data["commencementType"] = v
                elif "threshold" in kl:
                    detail_data["threshold"] = v
                elif "procurement technique" in kl:
                    detail_data["procurementTechnique"] = v
                elif "number of stages" in kl:
                    detail_data["numberOfStages"] = v
                elif "evaluation mechanism" in kl:
                    detail_data["evaluationMechanism"] = v
                elif "ceec preference type" in kl:
                    detail_data["ceecPreferenceType"] = v
                elif "framework agreement" in kl:
                    detail_data["frameworkAgreement"] = v
                elif "postqualification" in kl:
                    detail_data["postqualification"] = v
                elif "payment type" in kl:
                    detail_data["paymentType"] = v
                elif "payment amount" in kl:
                    detail_data["paymentAmount"] = v
                elif "payment terms" in kl:
                    detail_data["paymentTerms"] = v
                elif "bid security type" in kl:
                    detail_data["bidSecurityType"] = v
                elif "bid security amount" in kl and "type" not in kl:
                    detail_data["bidSecurityAmount"] = v
                elif "bid security amount type" in kl:
                    detail_data["bidSecurityAmountType"] = v
                elif "lot name" in kl:
                    if v and v not in detail_data["lots"]:
                        detail_data["lots"].append(v)
                elif "deadline for bid submission" in kl:
                    detail_data["closingDate"] = parse_zambia_date(v)
                    detail_data["closingDateRaw"] = v
                elif "bid submission deadline in" in kl:
                    detail_data["deadlineRemaining"] = v
                elif "end of clarification period" in kl:
                    detail_data["clarificationDeadline"] = parse_zambia_date(v)
                elif "bid opening date" in kl:
                    detail_data["bidOpeningDate"] = parse_zambia_date(v)
                elif "date of publication" in kl:
                    detail_data["publishDate"] = parse_zambia_date(v)
                elif "contract notice date" in kl:
                    detail_data["contractNoticeDate"] = parse_zambia_date(v)

            # Extract UNSPSC Codes if separated by br
            unspsc_match = re.search(r"<dt[^>]*>UNSPSC Codes:</dt>\s*<dd[^>]*>(.*?)</dd>", html, re.DOTALL | re.I)
            if unspsc_match:
                codes = [clean_text(c) for c in re.split(r"<br/?>", unspsc_match.group(1)) if clean_text(c)]
                detail_data["unspscCodes"] = codes

        # 2. Add Notice PDF to documents list
        detail_data["documents"].append({
            "documentId": f"notice-{clean_id}",
            "tenderId": clean_id,
            "title": f"Tender Notice ({clean_id})",
            "fileName": f"{clean_id}_Tender_Notice.pdf",
            "downloadUrl": f"{NOTICE_PDF_URL}?resourceId={clean_id}",
            "description": "Official ZPPA Gazetted Tender Notice PDF",
            "type": "Tender Notice PDF",
        })

        # 3. Fetch attached contract documents
        docs_url = f"{LIST_DOCS_URL}?resourceId={clean_id}"
        r_docs = requests.get(docs_url, headers=HEADERS, timeout=15)
        if r_docs.status_code == 200:
            doc_html = r_docs.text
            tbody_m = re.search(r"<table id=[\"']T02[\"'][^>]*>.*?<tbody>(.*?)</tbody>", doc_html, re.DOTALL | re.I)
            if tbody_m:
                d_rows = re.findall(r"<tr>(.*?)</tr>", tbody_m.group(1), re.DOTALL | re.I)
                for d_row in d_rows:
                    cells = re.findall(r"<td[^>]*>(.*?)</td>", d_row, re.DOTALL | re.I)
                    if len(cells) < 3:
                        continue
                    addendum_id = clean_text(cells[0])
                    doc_title = clean_text(cells[1])

                    # Extract file link & documentId
                    file_cell = cells[2]
                    file_m = re.search(r"<a[^>]*>(.*?)</a>", file_cell, re.DOTALL | re.I)
                    file_name = clean_text(file_m.group(1)) if file_m else clean_text(file_cell)

                    # Extract docId from downloadDocForAnonymous('30054490') or href
                    doc_id_m = re.search(r"downloadDocForAnonymous\(['\"]?(\d+)['\"]?\)", file_cell)
                    if not doc_id_m:
                        doc_id_m = re.search(r"documentId=(\d+)", file_cell)

                    doc_id = doc_id_m.group(1) if doc_id_m else str(len(detail_data["documents"]) + 1)
                    doc_desc = clean_text(cells[3]) if len(cells) > 3 else doc_title
                    doc_lang = clean_text(cells[4]) if len(cells) > 4 else "EN"

                    download_url = f"{DOWNLOAD_DOC_URL}?resourceId={clean_id}&documentId={doc_id}"

                    detail_data["documents"].append({
                        "documentId": doc_id,
                        "tenderId": clean_id,
                        "addendumId": addendum_id if addendum_id != "N/A" else None,
                        "title": doc_title or file_name,
                        "fileName": file_name,
                        "downloadUrl": download_url,
                        "description": doc_desc,
                        "language": doc_lang,
                        "type": "Contract Bidding Document (SBD)",
                    })

        # 4. Generate lineItems from Lots
        if detail_data["lots"]:
            for idx, lot in enumerate(detail_data["lots"], 1):
                detail_data["lineItems"].append({
                    "itemNumber": f"Lot {idx:02d}",
                    "description": lot,
                    "quantity": 1,
                    "unit": "Lot",
                    "specification": f"Procurement Lot {idx}: {lot}",
                })
        else:
            detail_data["lineItems"].append({
                "itemNumber": "Item 01",
                "description": detail_data.get("title") or detail_data.get("description") or "Procurement Package",
                "quantity": 1,
                "unit": "Package",
                "specification": detail_data.get("description") or "",
            })

    except Exception as ex:
        print(f"Error fetching detail for ZPPA resource {clean_id}: {ex}", file=sys.stderr)

    return detail_data


def upsert_to_supabase(records: list[dict]) -> bool:
    """Batches and upserts records into Supabase `tenders` table."""
    if not SUPABASE_URL or not SUPABASE_KEY:
        print("Error: Supabase credentials missing (NEXT_PUBLIC_SUPABASE_URL / SUPABASE_SECRET_KEY)", file=sys.stderr)
        return False

    headers = {
        "apikey": SUPABASE_KEY,
        "Authorization": f"Bearer {SUPABASE_KEY}",
        "Content-Type": "application/json",
        "Prefer": "resolution=merge-duplicates,return=minimal",
    }

    total = len(records)
    for i in range(0, total, 40):
        chunk = records[i:i + 40]
        url = f"{SUPABASE_URL.rstrip('/')}/rest/v1/tenders?on_conflict=id"
        try:
            res = requests.post(url, headers=headers, data=json.dumps(chunk), timeout=25)
            if res.status_code in (200, 201, 204):
                print(f"  Successfully upserted {i + len(chunk)}/{total} records to Supabase")
            else:
                print(f"  Failed upserting chunk {i}: {res.status_code} {res.text}", file=sys.stderr)
        except Exception as e:
            print(f"  Error communicating with Supabase: {e}", file=sys.stderr)
            return False

    return True


def build_supabase_record(tender_summary: dict, tender_detail: dict | None = None) -> dict:
    """Builds a complete Supabase tender row."""
    res_id = str(tender_summary["resource_id"])
    tender_id = f"zppa:{res_id}"

    title = tender_summary.get("title") or (tender_detail.get("title") if tender_detail else "")
    pe = tender_summary.get("procuring_entity") or (tender_detail.get("procuringEntity") if tender_detail else "ZPPA Procuring Entity")
    desc = tender_summary.get("description") or (tender_detail.get("description") if tender_detail else title)
    pub_date = tender_summary.get("publish_date") or (tender_detail.get("publishDate") if tender_detail else None)
    closing_date = tender_summary.get("closing_date") or (tender_detail.get("closingDate") if tender_detail else None)

    ref_no = (
        tender_detail.get("appReferenceNumber")
        if tender_detail and tender_detail.get("appReferenceNumber")
        else (
            tender_detail.get("tenderUniqueId")
            if tender_detail and tender_detail.get("tenderUniqueId")
            else f"ZPPA/{res_id}"
        )
    )

    documents = tender_detail.get("documents", []) if tender_detail else [
        {
            "documentId": f"notice-{res_id}",
            "tenderId": res_id,
            "title": f"Tender Notice ({res_id})",
            "fileName": f"{res_id}_Tender_Notice.pdf",
            "downloadUrl": f"{NOTICE_PDF_URL}?resourceId={res_id}",
            "description": "Official ZPPA Gazetted Tender Notice PDF",
            "type": "Tender Notice PDF",
        }
    ]

    line_items = tender_detail.get("lineItems", []) if tender_detail else [
        {
            "itemNumber": "Item 01",
            "description": title,
            "quantity": 1,
            "unit": "Lot",
            "specification": desc,
        }
    ]

    proc_type = (
        tender_detail.get("procurementType")
        if tender_detail and tender_detail.get("procurementType")
        else "Tender"
    )

    procedure = (
        tender_summary.get("procedure")
        or (tender_detail.get("procedure") if tender_detail else "Open Bidding National")
    )
    classification = classify_tender(title=title, description=desc, line_items=line_items)

    payload = {
        "id": tender_id,
        "tenderId": res_id,
        "refNo": ref_no,
        "title": title,
        "procuringEntity": pe,
        "country": "Zambia",
        "countryCode": "ZM",
        "location": "Zambia",
        "procurementType": proc_type,
        "procedure": procedure,
        "commodityGroup": classification.category,
        "publishDate": pub_date,
        "closingDate": closing_date,
        "sourcePortal": "ZPPA e-Procurement",
        "portal": "ZPPA",
        "sourceUrl": f"{VIEW_CFT_URL}?resourceId={res_id}",
        "detailsUrl": f"{VIEW_CFT_URL}?resourceId={res_id}",
        "pdfUrl": f"{NOTICE_PDF_URL}?resourceId={res_id}",
        "description": desc,
        "documents": documents,
        "lineItems": line_items,
        "lots": tender_detail.get("lots", []) if tender_detail else [],
        "rawFields": tender_detail.get("rawFields", {}) if tender_detail else {},
        "scrapedAt": datetime.now(timezone.utc).isoformat(),
    }
    payload.update(classification_payload(classification))

    # If detail was fetched, merge all rich fields
    if tender_detail:
        for k in [
            "appReferenceNumber",
            "tenderUniqueId",
            "procurementMethodRationale",
            "awardCriteriaDetails",
            "submissionMethodDetails",
            "commencementType",
            "threshold",
            "procurementTechnique",
            "numberOfStages",
            "evaluationMechanism",
            "ceecPreferenceType",
            "frameworkAgreement",
            "postqualification",
            "paymentType",
            "paymentAmount",
            "paymentTerms",
            "bidSecurityType",
            "bidSecurityAmount",
            "bidSecurityAmountType",
            "unspscCodes",
            "deadlineRemaining",
            "clarificationDeadline",
            "bidOpeningDate",
            "contractNoticeDate",
        ]:
            if k in tender_detail and tender_detail[k]:
                payload[k] = tender_detail[k]

    details = tender_detail if tender_detail else {
        "title": title,
        "description": desc,
        "officialUrl": f"{VIEW_CFT_URL}?resourceId={res_id}",
        "procuringEntity": pe,
        "documents": documents,
        "lineItems": line_items,
    }

    return {
        "id": tender_id,
        "source": "zppa",
        "source_id": res_id,
        "country": "ZM",
        "status": "live",
        "payload": payload,
        "details": details,
        "scraped_at": datetime.now(timezone.utc).isoformat(),
    }


def main():
    parser = argparse.ArgumentParser(description="ZPPA Zambia e-Procurement Scraper")
    parser.add_argument("--pages", type=int, default=5, help="Number of listing pages to scrape (0 for all)")
    parser.add_argument("--detail", type=str, help="Scrape and output JSON detail for a single resource ID")
    parser.add_argument("--enrich", action="store_true", help="Fetch complete details for every scraped listing")
    args = parser.parse_args()

    # If --detail requested, fetch single tender and print JSON
    if args.detail:
        res_id = args.detail.replace("zppa:", "").strip()
        print(f"Fetching ZPPA detail for resource ID: {res_id}...", file=sys.stderr)
        detail = fetch_tender_detail(res_id)
        print(json.dumps(detail, indent=2))
        return

    print("=== Starting ZPPA Zambia Live Tender Scraper ===")
    zppa = ZppaSession()
    success, initial_html = zppa.init_and_search(max_retries=4)
    if not success:
        print("Failed to initialize ZPPA search session after retries.", file=sys.stderr)
        sys.exit(1)

    # Detect total count and pages
    count_m = re.search(r"(\d+)\s+results in total", initial_html, re.I)
    total_results = int(count_m.group(1)) if count_m else 220
    total_pages = (total_results + 9) // 10
    print(f"Total live tenders available: {total_results} (~{total_pages} pages)")

    pages_to_scrape = total_pages if args.pages <= 0 else min(args.pages, total_pages)
    print(f"Scraping {pages_to_scrape} pages of active opportunities...")

    all_summaries = []
    seen_ids = set()

    for p in range(1, pages_to_scrape + 1):
        print(f"Fetching search results page {p}/{pages_to_scrape}...")
        html = initial_html if p == 1 else zppa.get_page(p)
        if not html:
            print(f"  Empty response for page {p}, skipping")
            continue

        page_tenders = parse_search_results_page(html)
        new_on_page = 0
        for t in page_tenders:
            rid = t["resource_id"]
            if rid not in seen_ids:
                seen_ids.add(rid)
                all_summaries.append(t)
                new_on_page += 1

        print(f"  Page {p}: found {len(page_tenders)} tenders ({new_on_page} new). Total collected: {len(all_summaries)}")
        time.sleep(0.4)

    print(f"\nCollected {len(all_summaries)} unique live tenders from ZPPA.")

    # Enrich with full details if --enrich or for top 15 recent tenders
    enriched_count = 0
    records = []
    for idx, s in enumerate(all_summaries):
        detail = None
        # Always enrich the first 15 live tenders with full details and attached SBDs
        if args.enrich or idx < 15:
            print(f"Enriching tender details [{idx + 1}/{len(all_summaries)}]: {s['resource_id']} - {s['title'][:45]}...")
            detail = fetch_tender_detail(s["resource_id"])
            enriched_count += 1
            time.sleep(0.2)

        record = build_supabase_record(s, detail)
        records.append(record)

    print(f"\nFinished preparing {len(records)} records ({enriched_count} fully enriched).")
    print(f"Upserting to Supabase (URL: {SUPABASE_URL})...")
    ok = upsert_to_supabase(records)
    if ok:
        print("=== ZPPA Zambia Live Tender Scraper Completed Successfully! ===")
    else:
        print("Warning: Upsert completed with errors or Supabase credentials missing.", file=sys.stderr)


if __name__ == "__main__":
    main()
