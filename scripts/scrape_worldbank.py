#!/usr/bin/env python3
"""
World Bank Procurement Notices Scraper for Zimbabwe and Zambia
Fetches official procurement notices directly from the World Bank API:
https://search.worldbank.org/api/v2/procnotices

Filters strictly by:
- Zimbabwe: project_ctry_name_exact=Zimbabwe (419 notices)
- Zambia: project_ctry_name_exact=Zambia (1800+ notices, fetches top 100 recent)

Extracts:
- Official World Bank Notice ID (e.g. OP00434937, OP00434950, OP00425362)
- Bid Reference Number (e.g. ZW-ZETDC-533247-CS-QCBS)
- Official Project Name & ID (e.g. Zimbabwe Renewable Energy Procurement Technical Assistance Project - P511160)
- True Procuring Entity (e.g. Zimbabwe Electricity Transmission and Distribution Company - ZETDC)
- Notice Type (Request for Expression of Interest, General Procurement Notice, Invitation for Bids, Contract Award)
- Procurement Method (Quality And Cost-Based Selection, Direct Selection, etc.)
- Submission Deadline & Publish Date
- Direct link to official World Bank procurement detail page:
  https://projects.worldbank.org/en/projects-operations/procurement-detail/{id}
- Upserts clean verified records directly to Supabase tenders table.
"""

import os
import sys
import re
import json
import time
import html
from datetime import datetime, timezone
from curl_cffi import requests
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

API_BASE = "https://search.worldbank.org/api/v2/procnotices"
DETAIL_BASE = "https://projects.worldbank.org/en/projects-operations/procurement-detail"

HEADERS = {
    "User-Agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"
}

def parse_iso_or_flexible_date(raw: str):
    if not raw:
        return None
    raw = raw.strip()
    # Try ISO
    try:
        dt = datetime.fromisoformat(raw.replace("Z", "+00:00"))
        return dt.astimezone(timezone.utc).isoformat()
    except:
        pass
    # Try 06-May-2026
    m = re.search(r"(\d{1,2})-([A-Za-z]{3})-(\d{4})", raw)
    if m:
        day, mon_str, year = m.groups()
        try:
            dt = datetime.strptime(f"{day}-{mon_str}-{year}", "%d-%b-%Y")
            return dt.replace(tzinfo=timezone.utc).isoformat()
        except:
            pass
    return None

def fetch_country_notices(country_name: str, country_code: str, max_count: int = 500):
    all_notices = []
    page_size = 100
    offset = 0

    print(f"\n[World Bank] Querying official notices for {country_name} ({country_code})...")

    while offset < max_count:
        url = f"{API_BASE}?format=json&rows={page_size}&os={offset}&project_ctry_name_exact={country_name}"
        try:
            resp = requests.get(url, headers=HEADERS, timeout=20, impersonate="chrome120")
            if resp.status_code != 200:
                print(f"  HTTP {resp.status_code} at offset {offset}")
                break

            data = resp.json()
            total_raw = data.get("total", 0)
            total = int(total_raw) if total_raw else 0
            if offset == 0:
                print(f"  Official total notices available on World Bank: {total}")

            notices = data.get("procnotices", [])
            if isinstance(notices, dict):
                notices = list(notices.values())

            if not notices:
                break

            all_notices.extend(notices)
            print(f"  Fetched {len(all_notices)} / {total} notices (offset {offset})")

            offset += page_size
            if offset >= total:
                break

            time.sleep(0.3)
        except Exception as e:
            print(f"  Exception fetching World Bank notices: {e}")
            break

    print(f"[World Bank] Total {country_name} notices retrieved: {len(all_notices)}")
    return all_notices

def build_records(notices, country_name: str, country_code: str):
    now_dt = datetime.now(timezone.utc)
    records = []
    seen_ids = set()

    for n in notices:
        nid = str(n.get("id") or "").strip()
        if not nid:
            continue

        record_id = f"worldbank:{country_code.lower()}:{nid}"
        source_id = f"{country_code.lower()}-{nid}"

        if record_id in seen_ids:
            continue
        seen_ids.add(record_id)

        bid_ref = n.get("bid_reference_no") or f"WB-{nid}"
        bid_desc = n.get("bid_description") or ""
        proj_name = n.get("project_name") or ""
        proj_id = n.get("project_id") or ""
        notice_type = n.get("notice_type") or "Procurement Notice"
        proc_method = n.get("procurement_method_name") or ""
        contact_org = n.get("contact_organization") or ""
        contact_name = n.get("contact_name") or ""
        contact_email = n.get("contact_email") or ""
        contact_phone = n.get("contact_phone_no") or ""
        contact_addr = n.get("contact_address") or ""

        # Title
        if bid_desc and len(bid_desc.strip()) > 3:
            title = html.unescape(bid_desc.strip())
        elif proj_name:
            title = f"{proj_name} - {notice_type}"
        else:
            title = f"World Bank Procurement Notice - {nid}"

        # Clean title
        title = re.sub(r"\s+", " ", title).strip()

        # Procuring Entity
        if contact_org and len(contact_org.strip()) > 3:
            entity = html.unescape(contact_org.strip())
        elif proj_name:
            entity = f"{proj_name} Implementing Entity"
        else:
            entity = f"Government of {country_name} (World Bank Project)"

        # Description
        raw_text = n.get("notice_text") or ""
        clean_formatted = re.sub(r"<\s*br\s*/?>", "\n", raw_text, flags=re.I)
        clean_formatted = re.sub(r"<\s*/?(p|div|tr|h[1-6]|li)[^>]*>", "\n\n", clean_formatted, flags=re.I)
        clean_formatted = re.sub(r"<[^>]+>", " ", clean_formatted)
        clean_formatted = html.unescape(clean_formatted)
        clean_lines = [re.sub(r"[ \t]+", " ", line).strip() for line in clean_formatted.splitlines()]
        clean_text = re.sub(r"\n{3,}", "\n\n", "\n".join(clean_lines)).strip()
        if not clean_text:
            clean_text = f"{notice_type} under {proj_name}. Reference: {bid_ref}. Procurement Method: {proc_method}."

        # Dates
        noticedate_str = n.get("noticedate") or n.get("submission_date") or ""
        deadline_str = n.get("submission_deadline_date") or ""

        publish_date = parse_iso_or_flexible_date(noticedate_str)
        closing_date = parse_iso_or_flexible_date(deadline_str)

        # Status: live if closing date in future OR published recently (2024-2026)
        pub_year = 0
        if noticedate_str:
            m_yr = re.search(r"\b(202[0-9])\b", noticedate_str)
            if m_yr:
                pub_year = int(m_yr.group(1))

        is_live = False
        if closing_date:
            try:
                c_dt = datetime.fromisoformat(closing_date.replace("Z", "+00:00"))
                if c_dt >= now_dt:
                    is_live = True
            except:
                pass

        if pub_year >= 2024:
            is_live = True

        status = "live" if is_live else "closed"

        official_url = f"{DETAIL_BASE}/{nid}"

        line_item = {
            "itemNumber": "Package 01",
            "description": title,
            "quantity": 1,
            "unit": "Contract / Assignment",
            "specification": clean_text[:400] if clean_text else title
        }
        classification = classify_tender(
            title=title,
            description=clean_text,
            scope=proj_name,
            line_items=[line_item],
        )

        notice_status = n.get("notice_status") or "Published"
        notice_lang = n.get("notice_lang_name") or "English"
        sub_time = n.get("submission_deadline_time") or ""
        deadline_formatted = f"{deadline_str.split('T')[0] if 'T' in deadline_str else deadline_str} {sub_time}".strip()

        notice_at_a_glance = {
            "projectId": proj_id,
            "projectTitle": proj_name,
            "country": country_name,
            "noticeNo": nid,
            "noticeType": notice_type,
            "noticeStatus": notice_status,
            "borrowerBidReference": bid_ref,
            "procurementMethod": proc_method,
            "language": notice_lang,
            "submissionDeadline": deadline_formatted,
            "submissionDeadlineDate": deadline_str,
            "submissionDeadlineTime": sub_time,
            "publishedDate": noticedate_str
        }

        contact_info = {
            "organization": entity,
            "name": contact_name,
            "address": contact_addr,
            "city": "Harare" if "harare" in contact_addr.lower() else ("Lusaka" if "lusaka" in contact_addr.lower() else ""),
            "province": country_name,
            "postalCode": "",
            "country": n.get("contact_ctry_name") or country_name,
            "phone": contact_phone,
            "email": contact_email,
            "website": ""
        }

        payload = {
            "id": record_id,
            "tenderId": nid,
            "refNo": bid_ref,
            "referenceNumber": bid_ref,
            "title": title,
            "procuringEntity": entity,
            "countryCode": country_code,
            "countryName": country_name,
            "categoryCodes": [proj_id] if proj_id else [],
            "estimatedValue": 0,
            "currency": "USD",
            "publishDate": publish_date,
            "closingDate": closing_date,
            "procurementType": notice_type,
            "procurementMethod": proc_method,
            "projectTitle": proj_name,
            "projectId": proj_id,
            "sourcePortal": "World Bank",
            "sourceUrl": official_url,
            "detailsUrl": official_url,
            "portalUrl": official_url,
            "description": clean_text[:4000],
            "contact": contact_info,
            "contactInfo": contact_info,
            "noticeAtAGlance": notice_at_a_glance,
            "noticeTextHtml": raw_text,
            "noticeStatus": notice_status,
            "noticeLang": notice_lang,
            "submissionDeadlineTime": sub_time,
            "documents": [],
            "lineItems": [line_item]
        }
        payload.update(classification_payload(classification))

        details = {
            "title": title,
            "tenderReferenceNumber": bid_ref,
            "procuringEntity": entity,
            "description": clean_text,
            "officialUrl": official_url,
            "closingDate": closing_date,
            "publishDate": publish_date,
            "procurementMethod": proc_method,
            "projectTitle": proj_name,
            "projectId": proj_id,
            "contact": contact_info,
            "contactInfo": contact_info,
            "noticeAtAGlance": notice_at_a_glance,
            "noticeTextHtml": raw_text,
            "noticeStatus": notice_status,
            "noticeLang": notice_lang,
            "submissionDeadlineTime": sub_time,
            "lineItems": [line_item],
            "documents": [],
            "aggregatorNotice": False
        }

        records.append({
            "id": record_id,
            "source": "worldbank",
            "source_id": source_id,
            "country": country_code,
            "status": status,
            "payload": payload,
            "details": details,
            "scraped_at": datetime.now(timezone.utc).isoformat()
        })

    return records

def upsert_to_supabase(records):
    if not SUPABASE_URL or not SUPABASE_KEY:
        print("[World Bank] Missing Supabase credentials, skipping database upload.")
        return

    headers = {
        "apikey": SUPABASE_KEY,
        "Authorization": f"Bearer {SUPABASE_KEY}",
        "Content-Type": "application/json",
        "Prefer": "resolution=merge-duplicates,return=minimal"
    }

    # Upsert in place. Deleting and reinserting every row makes unchanged tenders
    # look new and incorrectly fires the new-tender notification trigger again.
    print(f"\n[World Bank] Uploading {len(records)} verified World Bank records to Supabase...")
    batch_size = 25
    uploaded = 0
    for i in range(0, len(records), batch_size):
        batch = records[i:i + batch_size]
        url = f"{SUPABASE_URL}/rest/v1/tenders?on_conflict=id"
        try:
            resp = requests.post(url, headers=headers, json=batch)
            if resp.status_code in [200, 201]:
                uploaded += len(batch)
                print(f"  Batch {i // batch_size + 1}: Uploaded {len(batch)} records (Total: {uploaded}/{len(records)})")
            else:
                print(f"  Batch {i // batch_size + 1} failed: HTTP {resp.status_code} - {resp.text[:200]}")
        except Exception as e:
            print(f"  Batch {i // batch_size + 1} exception: {e}")

    print(f"[World Bank] Successfully synchronized {uploaded} World Bank procurement records!")

def main():
    # 1. Fetch Zimbabwe (all 419 notices)
    zw_notices = fetch_country_notices("Zimbabwe", "ZW", max_count=500)
    zw_records = build_records(zw_notices, "Zimbabwe", "ZW")

    # 2. Fetch Zambia (top 100 notices)
    zm_notices = fetch_country_notices("Zambia", "ZM", max_count=100)
    zm_records = build_records(zm_notices, "Zambia", "ZM")

    all_records = zw_records + zm_records
    print(f"\n[World Bank] Built {len(all_records)} total verified records ({len(zw_records)} Zimbabwe, {len(zm_records)} Zambia).")

    # Sample check
    print("\n--- Sample Top Zimbabwe World Bank Notices ---")
    for r in zw_records[:4]:
        p = r["payload"]
        print(f"[{r['country']}] Ref: {p['refNo']} | Entity: {p['procuringEntity']} | Status: {r['status']}")
        print(f"   Title: {p['title']}")
        print(f"   Published: {p['publishDate']} | Deadline: {p['closingDate']} | URL: {p['portalUrl']}")

    upsert_to_supabase(all_records)

if __name__ == "__main__":
    main()
