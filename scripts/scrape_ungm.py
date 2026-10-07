#!/usr/bin/env python3
"""
UN Global Marketplace (UNGM) Scraper for Zimbabwe and Zambia
Fetches live notices from https://www.ungm.org/Public/Notice/Search for:
- Zimbabwe (UNGM Country ID: 2520)
- Zambia (UNGM Country ID: 2519)
Extracts:
- Official Reference Number (e.g. RFP-DAN-2026-503933, EOIUNPD24254, UNHCR RFP 3510, WHO-SHQ-RFP-26-2032)
- Clean Title & UN Procuring Entity (UNICEF, WHO, UNHCR, UN Secretariat, UNDP, FAO, etc.)
- Opportunity Type (Request for Proposal, Request for EOI, Invitation to Bid)
- Exact Closing Date / Deadline
- Direct downloadable bidding documents (PDFs, DOCX, XLSX)
- Upserts directly to Supabase tenders table.
"""

import os
import sys
import re
import json
import time
import html
from datetime import datetime, timezone
from curl_cffi import requests
from concurrent.futures import ThreadPoolExecutor
from tender_classifier import classification_payload, classify_tender

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

SEARCH_URL = "https://www.ungm.org/Public/Notice/Search"
BASE_NOTICE_URL = "https://www.ungm.org/Public/Notice"

HEADERS = {
    "Content-Type": "application/json",
    "X-Requested-With": "XMLHttpRequest",
    "Referer": "https://www.ungm.org/Public/Notice",
    "User-Agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
}

def parse_ungm_date(raw: str):
    if not raw:
        return None
    raw = raw.strip()
    m = re.search(r"(\d{1,2})-([A-Za-z]{3})-(\d{4})\s*(\d{1,2}):(\d{2})", raw)
    if m:
        day, mon_str, year, hr, mn = m.groups()
        try:
            dt = datetime.strptime(f"{day}-{mon_str}-{year} {hr}:{mn}", "%d-%b-%Y %H:%M")
            return dt.replace(tzinfo=timezone.utc).isoformat()
        except:
            pass
    m2 = re.search(r"(\d{1,2})-([A-Za-z]{3})-(\d{4})", raw)
    if m2:
        day, mon_str, year = m2.groups()
        try:
            dt = datetime.strptime(f"{day}-{mon_str}-{year}", "%d-%b-%Y")
            return dt.replace(tzinfo=timezone.utc).isoformat()
        except:
            pass
    return None

def fetch_ungm_notices(country_id: str, country_name: str, country_code: str, max_pages: int = 25):
    print(f"Fetching UNGM active notices for {country_name} (Country ID: {country_id}) across all pages...")
    notices = []
    seen_ids = set()

    for page in range(max_pages):
        payload = {
            "PageIndex": page,
            "PageSize": 15,
            "Title": "",
            "Description": "",
            "Reference": "",
            "PublishedFrom": "",
            "PublishedTo": "",
            "DeadlineFrom": "",
            "DeadlineTo": "",
            "Countries": [country_id] if country_id else [],
            "Agencies": [],
            "UNSPSCs": [],
            "NoticeTypes": [],
            "SortField": "Deadline",
            "SortAscending": False,
            "isPicker": False,
            "IsSustainable": False,
            "IsActive": True
        }

        try:
            resp = requests.post(SEARCH_URL, json=payload, headers=HEADERS, impersonate="chrome120", timeout=20)
            if resp.status_code != 200:
                print(f"  Page {page}: HTTP {resp.status_code}, stopping pagination.")
                break

            blocks = re.split(r'<div role=\"row\"[^>]*data-noticeid=\"(\d+)\"', resp.text)[1:]
            if not blocks:
                print(f"  Page {page}: No more notices found, ending pagination.")
                break

            page_count = 0
            for i in range(0, len(blocks), 2):
                nid = blocks[i]
                if nid in seen_ids:
                    continue
                seen_ids.add(nid)

                content = blocks[i + 1]

                title_m = re.search(r'class=[\'\"]ungm-title[^\"]*[\'\"]>(.*?)</span>', content, re.DOTALL)
                title = html.unescape(re.sub(r'<[^>]+>', '', title_m.group(1)).strip()) if title_m else "UN Procurement Notice"

                agency_m = re.search(r'class=[\'\"]tableCell resultAgency[\'\"]>(.*?)</div>', content, re.DOTALL)
                agency = html.unescape(re.sub(r'<[^>]+>', '', agency_m.group(1)).strip()) if agency_m else "United Nations"

                ref_m = re.search(r'data-description=[\'\"]Reference[\'\"]>(.*?)</div>', content, re.DOTALL)
                ref_no = html.unescape(re.sub(r'<[^>]+>', '', ref_m.group(1)).strip()) if ref_m else f"UNGM-{nid}"

                type_m = re.search(r'<label for=[\'\"][^\'\"]*[\'\"]>([^<]+)</label>', content)
                opp_type = html.unescape(type_m.group(1).strip()) if type_m else "Request for Proposal"

                dl_m = re.search(r'data-description=[\'\"]Deadline[\'\"]>(.*?)</div>', content, re.DOTALL)
                raw_deadline = re.sub(r'<[^>]+>', ' ', dl_m.group(1)).strip() if dl_m else ""
                closing_date = parse_ungm_date(raw_deadline)

                pub_m = re.search(r'<div role=[\'\"]cell[\'\"] class=[\'\"]tableCell[\'\"]>\s*<span>\s*(\d{1,2}-[A-Za-z]{3}-\d{4})', content)
                raw_published = pub_m.group(1).strip() if pub_m else ""
                publish_date = parse_ungm_date(raw_published)

                notices.append({
                    "noticeId": nid,
                    "refNo": ref_no,
                    "title": title,
                    "agency": agency,
                    "opportunityType": opp_type,
                    "rawDeadline": raw_deadline,
                    "closingDate": closing_date,
                    "publishDate": publish_date,
                    "countryName": country_name,
                    "countryCode": country_code
                })
                page_count += 1

            print(f"  Page {page}: Extracted {page_count} notices (Total so far: {len(notices)})")
            if page_count == 0:
                break
        except Exception as ex:
            print(f"Error fetching UNGM page {page} for {country_name}: {ex}")
            break

    print(f"Finished {country_name}: Extracted {len(notices)} total active notices across {page + 1} pages.")
    return notices

def fetch_notice_detail(notice):
    nid = notice["noticeId"]
    url = f"{BASE_NOTICE_URL}/{nid}"
    try:
        resp = requests.get(url, headers=HEADERS, impersonate="chrome120", timeout=15)
        if resp.status_code != 200:
            notice["description"] = notice["title"]
            notice["documents"] = []
            return notice

        # Extract Description
        desc = ""
        m_desc = re.search(r'<div class=[\'\"]title[\'\"]>Description</div>\s*<div[^>]*>(.*?)</div>', resp.text, re.DOTALL | re.I)
        if m_desc:
            desc = html.unescape(re.sub(r'<[^>]+>', ' ', m_desc.group(1)).strip())
        if not desc:
            dp = re.search(r'<div[^>]*class=[\'\"][^\'\"]*noticeDescription[^\'\"]*[\'\"][^>]*>(.*?)</div>', resp.text, re.DOTALL)
            if dp:
                desc = html.unescape(re.sub(r'<[^>]+>', ' ', dp.group(1)).strip())
        if not desc:
            desc = notice["title"]
        notice["description"] = desc

        # Extract Documents
        docs = []
        seen_doc_ids = set()
        for m in re.finditer(r'href=[\'\"](/Public/Notice/DownloadDocument\?noticeId=(\d+)&amp;documentId=(\d+))[\'\"][^>]*>([^<]+)</a>', resp.text):
            doc_id = m.group(3)
            if doc_id in seen_doc_ids:
                continue
            seen_doc_ids.add(doc_id)
            doc_name = html.unescape(m.group(4).strip())
            download_url = f"https://www.ungm.org/Public/Notice/DownloadDocument?noticeId={nid}&documentId={doc_id}"
            docs.append({
                "documentId": doc_id,
                "tenderId": nid,
                "title": doc_name,
                "fileName": doc_name,
                "downloadUrl": download_url
            })

        notice["documents"] = docs
        return notice
    except Exception as ex:
        notice["description"] = notice["title"]
        notice["documents"] = []
        return notice

def build_supabase_records(notices):
    now_iso = datetime.now(timezone.utc).isoformat()
    now_dt = datetime.now(timezone.utc)
    records = []
    seen_ids = set()

    for idx, item in enumerate(notices):
        nid = str(item["noticeId"]).strip()
        ref_no = item["refNo"]
        title = item["title"]
        agency = item["agency"]
        desc = item.get("description") or title
        closing_date = item.get("closingDate")
        publish_date = item.get("publishDate")
        country_code = item["countryCode"]
        country_name = item["countryName"]
        docs = item.get("documents") or []

        line_items = [
            {
                "itemNumber": "Package 01",
                "description": title,
                "quantity": 1,
                "unit": "Lot",
                "specification": desc,
            }
        ]
        classification = classify_tender(title=title, description=desc, line_items=line_items)

        is_live = True
        if closing_date:
            try:
                closing_dt = datetime.fromisoformat(closing_date.replace("Z", "+00:00"))
                is_live = closing_dt >= now_dt
            except:
                is_live = True

        status = "live" if is_live else "closed"
        record_id = f"ungm:{country_code.lower()}:{nid}"
        source_id = f"{country_code.lower()}-{nid}"

        if record_id in seen_ids:
            continue
        seen_ids.add(record_id)

        notice_url = f"{BASE_NOTICE_URL}/{nid}"

        payload = {
            "id": record_id,
            "tenderId": nid,
            "refNo": ref_no,
            "referenceNumber": ref_no,
            "contractNumber": ref_no,
            "title": title,
            "procuringEntity": agency,
            "country": country_name,
            "countryCode": country_code,
            "location": f"{country_name} (UN Operations)",
            "commodityGroup": classification.category,
            "procurementType": item.get("opportunityType", "Request for Proposal"),
            "publishDate": publish_date,
            "closingDate": closing_date,
            "sourcePortal": "UN Global Marketplace (UNGM)",
            "portal": "UNGM",
            "sourceUrl": notice_url,
            "detailsUrl": notice_url,
            "portalUrl": notice_url,
            "description": desc,
            "documents": docs,
            "lineItems": line_items,
        }
        payload.update(classification_payload(classification))

        details = {
            "title": title,
            "tenderReferenceNumber": ref_no,
            "procuringEntity": agency,
            "description": desc,
            "officialUrl": notice_url,
            "closingDate": closing_date,
            "procurementMethod": payload["procurementType"],
            "lineItems": payload["lineItems"],
            "documents": docs,
            "aggregatorNotice": False
        }

        records.append({
            "id": record_id,
            "source": "ungm",
            "source_id": source_id,
            "country": country_code,
            "status": status,
            "payload": payload,
            "details": details,
            "scraped_at": now_iso
        })

    return records

def upsert_to_supabase(records):
    if not SUPABASE_URL or not SUPABASE_KEY:
        print("Missing Supabase credentials, skipping database upload.")
        return

    headers = {
        "apikey": SUPABASE_KEY,
        "Authorization": f"Bearer {SUPABASE_KEY}",
        "Content-Type": "application/json",
        "Prefer": "resolution=merge-duplicates,return=minimal"
    }

    # Upsert in place so unchanged notices do not fire the insert notification
    # trigger again on every synchronization run.
    print(f"Uploading {len(records)} verified UNGM records to Supabase...")
    batch_size = 25
    for i in range(0, len(records), batch_size):
        batch = records[i:i + batch_size]
        url = f"{SUPABASE_URL}/rest/v1/tenders?on_conflict=id"
        try:
            resp = requests.post(url, headers=headers, json=batch)
            if resp.status_code in [200, 201]:
                print(f"  Batch {i // batch_size + 1}: Uploaded {len(batch)} records (Status {resp.status_code})")
            else:
                print(f"  Batch {i // batch_size + 1} failed: HTTP {resp.status_code} - {resp.text[:200]}")
        except Exception as e:
            print(f"  Batch {i // batch_size + 1} exception: {e}")

    print("UNGM synchronization complete!")

def main():
    # 1. Fetch Zimbabwe (2520) & Zambia (2519)
    zw_notices = fetch_ungm_notices("2520", "Zimbabwe", "ZW")
    zm_notices = fetch_ungm_notices("2519", "Zambia", "ZM")
    all_notices = zw_notices + zm_notices

    print(f"Total UN notices discovered: {len(all_notices)}. Fetching detail pages and document attachments concurrently...")
    enriched = []
    with ThreadPoolExecutor(max_workers=6) as executor:
        enriched = list(executor.map(fetch_notice_detail, all_notices))

    records = build_supabase_records(enriched)
    print(f"Built {len(records)} verified UNGM tender records.")

    for r in records[:5]:
        p = r["payload"]
        print(f"[{r['country']}] Ref: {p['refNo']} | Agency: {p['procuringEntity']} | Close: {p['closingDate']} | Docs: {len(p['documents'])}")
        print(f"   Title: {p['title']}")

    upsert_to_supabase(records)

if __name__ == "__main__":
    if len(sys.argv) > 2 and sys.argv[1] == "--detail":
        target = sys.argv[2]
        nid = target.split("/")[-1].replace("ungm:", "")
        res = fetch_notice_detail({"noticeId": nid, "title": ""})
        print(json.dumps(res))
    else:
        main()
