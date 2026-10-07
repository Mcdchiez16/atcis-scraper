#!/usr/bin/env python3
"""
African Development Bank (AfDB) Zimbabwe Procurement Scraper
Uses curl_cffi with Chrome TLS impersonation to bypass Cloudflare protection.
Scrapes all procurement notices (EOIs, SPNs, Awards) for Zimbabwe (tid=343)
and saves them into Supabase with direct PDF links and full descriptions.
"""

import os
import sys
import re
import json
import time
import urllib.parse
from datetime import datetime
from curl_cffi import requests
from tender_classifier import classification_payload, classify_tender
# Load Supabase configuration directly without external dotenv
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

AFDB_BASE = "https://www.afdb.org"
PROCUREMENT_LIST_URL = f"{AFDB_BASE}/en/projects-and-operations/procurement?tid=343"

def clean_html_text(text: str) -> str:
    if not text:
        return ""
    # Strip tags
    text = re.sub(r"<[^>]+>", " ", text)
    # Decode html entities
    text = text.replace("&amp;", "&").replace("&quot;", "\"").replace("&#39;", "'").replace("&copy;", "©")
    # Normalize spaces
    return re.sub(r"\s+", " ", text).strip()

def extract_tender_type(title: str) -> str:
    upper = title.upper()
    if upper.startswith("EOI") or "EXPRESSION OF INTEREST" in upper:
        return "EOI - Expression of Interest"
    if upper.startswith("SPN") or "SPECIFIC PROCUREMENT NOTICE" in upper:
        return "SPN - Specific Procurement Notice"
    if "CONTRACT AWARD" in upper or "AWARD" in upper:
        return "Contract Award"
    if "RFP" in upper or "REQUEST FOR PROPOSAL" in upper:
        return "RFP - Request for Proposals"
    if "GPN" in upper or "GENERAL PROCUREMENT NOTICE" in upper:
        return "GPN - General Procurement Notice"
    return "Tender Notice"

def extract_commodity_group(title: str) -> str:
    lower = title.lower()
    if any(k in lower for k in ["consultan", "audit", "study", "assessment", "plan", "review"]):
        return "Consulting & Professional Services"
    if any(k in lower for k in ["ict", "software", "sensor", "logger", "computer", "network", "server"]):
        return "ICT & Telecommunications"
    if any(k in lower for k in ["machinery", "equipment", "agro", "processing", "supply", "goods"]):
        return "Industrial & Agricultural Equipment"
    if any(k in lower for k in ["rehabilitation", "dam", "civil", "construction", "works", "water", "infrastructure"]):
        return "Civil Works & Infrastructure"
    return "General Procurement"

def fetch_single_detail(doc_url: str):
    """Fetch detail page to get description and PDF URL."""
    try:
        r = requests.get(doc_url, impersonate="chrome124", timeout=12)
        if r.status_code != 200:
            return None, None
        html = r.text

        # 1. Direct PDF URL
        pdf_url = ""
        pdf_m = re.search(r"data-src=[\"\x27]([^\"]+\.pdf[^\"]*)[\"\x27]", html, re.I)
        if not pdf_m:
            pdf_m = re.search(r"file=([^&\"]+\.pdf[^\"]*)", html, re.I)
            if pdf_m:
                pdf_url = urllib.parse.unquote(pdf_m.group(1))
        else:
            pdf_url = pdf_m.group(1)

        if pdf_url and not pdf_url.startswith("http"):
            pdf_url = AFDB_BASE + pdf_url

        # 2. Extract description paragraphs
        paras = []
        for p in re.findall(r"<p>(.*?)</p>", html, re.DOTALL):
            c = clean_html_text(p)
            if (
                c
                and not c.startswith("©")
                and not c.startswith("&copy;")
                and not c.startswith("Submit a document")
                and not c.startswith("Resources for")
                and not c.startswith("All rights reserved")
                and len(c) > 20
            ):
                paras.append(c)
        desc = " ".join(paras)
        if not desc:
            meta_m = re.search(r"<meta\s+name=[\"\x27]description[\"\x27]\s+content=[\"\x27]([^\"]+)[\"\x27]", html, re.I)
            if not meta_m:
                meta_m = re.search(r"<meta\s+property=[\"\x27]og:description[\"\x27]\s+content=[\"\x27]([^\"]+)[\"\x27]", html, re.I)
            if meta_m:
                desc = clean_html_text(meta_m.group(1))

        return desc, pdf_url
    except Exception as e:
        return None, None

def scrape_afdb_page(page_num: int):
    url = f"{PROCUREMENT_LIST_URL}&page={page_num}"
    r = requests.get(url, impersonate="chrome124", timeout=15)
    if r.status_code != 200:
        print(f"Error fetching page {page_num}: status {r.status_code}", file=sys.stderr)
        return []

    html = r.text
    blocks = html.split("<div class=\" col-xs-12 col-sm-12 col-md-4 col-lg-4\">")[1:]
    items = []

    for b in blocks:
        t_m = re.search(r"views-field-title.*?<a href=[\"\x27](/en/documents/[^\"]+)[\"\x27]>([^<]+)</a>", b, re.DOTALL)
        d_m = re.search(r"class=[\"\x27]date-display-single[\"\x27][^>]*>([^<]+)</span>", b)
        if not t_m:
            continue

        raw_path = t_m.group(1).strip()
        slug = raw_path.split("/")[-1]
        title = clean_html_text(t_m.group(2))
        pub_date_raw = d_m.group(1).strip() if d_m else ""
        doc_url = AFDB_BASE + raw_path

        # Parse publication date
        publish_date_iso = ""
        closing_date_iso = ""
        try:
            # Format: "18-Aug-2026"
            dt = datetime.strptime(pub_date_raw, "%d-%b-%Y")
            publish_date_iso = dt.strftime("%Y-%m-%d")
            # Default closing date 30 days after publication if unspecified
            closing_date_iso = datetime.fromtimestamp(dt.timestamp() + 30 * 86400).strftime("%Y-%m-%d")
        except Exception:
            publish_date_iso = pub_date_raw

        proc_type = extract_tender_type(title)
        commodity = extract_commodity_group(title)

        items.append({
            "id": f"afdb:{slug}",
            "slug": slug,
            "title": title,
            "publish_date": publish_date_iso,
            "closing_date": closing_date_iso,
            "doc_url": doc_url,
            "procurement_type": proc_type,
            "commodity_group": commodity,
        })

    return items

def upsert_to_supabase(records):
    if not SUPABASE_URL or not SUPABASE_KEY:
        print("Supabase credentials missing", file=sys.stderr)
        return False

    headers = {
        "apikey": SUPABASE_KEY,
        "Authorization": f"Bearer {SUPABASE_KEY}",
        "Content-Type": "application/json",
        "Prefer": "resolution=merge-duplicates,return=minimal",
    }

    # Batch into chunks of 50
    for i in range(0, len(records), 50):
        chunk = records[i:i+50]
        url = f"{SUPABASE_URL.rstrip('/')}/rest/v1/tenders?on_conflict=id"
        res = requests.post(url, headers=headers, data=json.dumps(chunk))
        if res.status_code not in (200, 201, 204):
            print(f"Failed upserting chunk {i}: {res.status_code} {res.text}", file=sys.stderr)
        else:
            print(f"Upserted chunk {i} to {i + len(chunk)}")
    return True

def main():
    if len(sys.argv) > 2 and sys.argv[1] == "--detail":
        arg = sys.argv[2].replace("afdb:", "")
        if arg.startswith("http"):
            doc_url = arg
        elif "/" in arg:
            doc_url = f"{AFDB_BASE}/{arg.lstrip('/')}"
        else:
            doc_url = f"{AFDB_BASE}/en/documents/{arg}"
        desc, pdf_url = fetch_single_detail(doc_url)
        print(json.dumps({"description": desc or "", "pdf_url": pdf_url or ""}))
        return

    print("--- Starting AfDB Zimbabwe Scraper ---")
    all_notices = []
    
    # Scrape all 12 pages (0 to 11)
    for p in range(12):
        print(f"Fetching listing page {p}...")
        notices = scrape_afdb_page(p)
        if not notices:
            print(f"No notices on page {p}, stopping pagination.")
            break
        all_notices.extend(notices)
        print(f"  Page {p}: {len(notices)} notices found. (Total so far: {len(all_notices)})")
        time.sleep(0.3)

    print(f"Total AfDB Zimbabwe notices collected: {len(all_notices)}")

    # Fetch details and PDFs for ALL notices concurrently using ThreadPoolExecutor
    print(f"Fetching full details & PDFs for all {len(all_notices)} notices concurrently...")
    from concurrent.futures import ThreadPoolExecutor

    def process_item_detail(idx_item):
        idx, item = idx_item
        try:
            desc, pdf = fetch_single_detail(item["doc_url"])
            if desc:
                item["description"] = desc
            if pdf:
                item["pdf_url"] = pdf
            if (idx + 1) % 25 == 0 or idx == len(all_notices) - 1:
                print(f"  Processed {idx + 1}/{len(all_notices)} details (PDF found: {bool(pdf)})")
        except Exception as e:
            pass
        return item

    with ThreadPoolExecutor(max_workers=8) as executor:
        all_notices = list(executor.map(process_item_detail, enumerate(all_notices)))

    # Prepare Supabase records
    supabase_rows = []
    for item in all_notices:
        slug = item["slug"]
        pdf = item.get("pdf_url")
        desc = item.get("description") or f"African Development Bank (AfDB) official procurement requirement for Zimbabwe: {item['title']}."
        
        docs = []
        if pdf:
            pdf_filename = pdf.split("/")[-1]
            docs.append({
                "documentId": f"afdb-doc-{slug}",
                "tenderId": slug,
                "title": pdf_filename,
                "fileName": pdf_filename,
                "downloadUrl": pdf,
            })

        payload = {
            "id": f"afdb:{slug}",
            "tenderId": slug,
            "refNo": f"AfDB-ZW-{slug[:18].upper()}",
            "title": item["title"],
            "procuringEntity": "African Development Bank (AfDB)",
            "country": "Zimbabwe",
            "countryCode": "ZW",
            "location": "Zimbabwe",
            "procurementType": item["procurement_type"],
            "commodityGroup": item["commodity_group"],
            "publishDate": item["publish_date"],
            "closingDate": item["closing_date"],
            "sourcePortal": "AfDB",
            "portal": "AfDB",
            "sourceUrl": item["doc_url"],
            "detailsUrl": item["doc_url"],
            "description": desc,
            "documents": docs,
            "lineItems": [
                {
                    "itemNumber": "Package 01",
                    "description": item["title"],
                    "quantity": 1,
                    "unit": "Lot",
                    "specification": desc
                }
            ]
        }
        classification = classify_tender(
            title=item["title"],
            description=desc,
            line_items=payload["lineItems"],
        )
        payload.update(classification_payload(classification))

        details = {
            "title": item["title"],
            "description": desc,
            "officialUrl": item["doc_url"],
            "procuringEntity": "African Development Bank (AfDB)",
            "documents": docs,
            "lineItems": payload["lineItems"]
        }

        supabase_rows.append({
            "id": f"afdb:{slug}",
            "source": "afdb",
            "source_id": slug,
            "country": "ZW",
            "status": "live",
            "payload": payload,
            "details": details,
            "scraped_at": datetime.utcnow().isoformat() + "Z"
        })

    # Deduplicate IDs to ensure PostgreSQL ON CONFLICT does not encounter duplicate keys in the same batch
    seen_ids = {}
    deduped_rows = []
    for row in supabase_rows:
        rid = row["id"]
        if rid in seen_ids:
            seen_ids[rid] += 1
            new_id = f"{rid}-{seen_ids[rid]}"
            row["id"] = new_id
            row["source_id"] = f"{row['source_id']}-{seen_ids[rid]}"
            row["payload"]["id"] = new_id
        else:
            seen_ids[rid] = 1
        deduped_rows.append(row)

    print(f"Upserting {len(deduped_rows)} AfDB records into Supabase...")
    upsert_to_supabase(deduped_rows)
    print("AfDB Zimbabwe procurement synchronization completed successfully!")

if __name__ == "__main__":
    main()
