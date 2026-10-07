#!/usr/bin/env python3
"""
OnlineTenders Zimbabwe Scraper
Uses curl_cffi with Chrome TLS impersonation to extract all notices from
https://www.onlinetenders.co.za/tenders/zimbabwe.
Extracts:
- Real Contract Number (e.g. ZIMDEF 35/2026, 7000008750, COB/WKD/...)
- Accurate Procuring Entity (e.g. Zimbabwe Manpower Development Fund (ZIMDEF), City of Bulawayo, etc.)
- Clean, professional Title
- Accurate Closing Date (parsed from YYYY-MM-DD HH[hH]MM)
- Live vs Closed status
- Upserts directly to Supabase tenders table.
"""

import os
import sys
import re
import json
import time
from datetime import datetime, timezone, timedelta
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

BASE_URL = "https://www.onlinetenders.co.za/tenders/zimbabwe"

def parse_closing_date(raw_date: str):
    if not raw_date:
        return None
    raw_date = raw_date.strip()
    m = re.search(r"(\d{4}-\d{2}-\d{2})\s*(\d{1,2})[hH:](\d{2})", raw_date)
    if m:
        return f"{m.group(1)}T{int(m.group(2)):02d}:{m.group(3)}:00Z"
    m2 = re.search(r"(\d{4}-\d{2}-\d{2})", raw_date)
    if m2:
        return f"{m2.group(1)}T23:59:59Z"
    return None

def infer_procuring_entity(cn: str, desc: str) -> str:
    text = f"{cn} {desc}".lower()

    if re.search(r"\bnpa\b", text) or "prosecuting authority" in text:
        return "National Prosecuting Authority (NPA)"
    if "zimdef" in text:
        return "Zimbabwe Manpower Development Fund (ZIMDEF)"
    if "cob/" in text or "bulawayo" in text or "thorngrove" in text:
        return "City of Bulawayo"
    if "mrdc/" in text or "marondera" in text:
        return "Marondera Rural District Council (MRDC)"
    if "7000008750" in text or "meps" in text or "distribution transformers in eac" in text:
        return "Regional Centre for Renewable Energy (EACREEE / SACREEE)"
    if re.search(r"\bmohcc\b", text) or "ministry of health" in text:
        return "Ministry of Health & Child Care (MOHCC)"
    if re.search(r"\bpsc\b", text) or "public service commission" in text:
        return "Public Service Commission (PSC)"
    if "cog/" in text or re.search(r"\bcog\b", text) or "gweru" in text:
        return "City of Gweru"
    if "kdmc" in text or "kwekwe" in text:
        return "Kwekwe City Council (KDMC)"
    if "com/" in text or re.search(r"\bcom\b", text) or "masvingo" in text:
        return "City of Masvingo"
    if "timb" in text or "tobacco industry" in text:
        return "Tobacco Industry & Marketing Board (TIMB)"
    if "vfcc" in text or "victoria falls" in text:
        return "Victoria Falls City Council (VFCC)"
    if "chit/" in text or re.search(r"\bchit\b", text) or "chitungwiza" in text:
        return "Chitungwiza Municipality"
    if "gzu" in text or "great zimbabwe university" in text:
        return "Great Zimbabwe University (GZU)"
    if "znr" in text or "zinara" in text or "road administration" in text:
        return "Zimbabwe National Road Administration (ZINARA)"
    if "pzl" in text:
        return "Petrotrade Zimbabwe (PZL)"
    if "ref/" in text or "rural electrification" in text:
        return "Rural Electrification Fund (REF)"
    if "ruwa" in text:
        return "Ruwa Local Board"
    if "ncc/" in text or "norton" in text:
        return "Norton Town Council (NCC)"
    if "mm/" in text or "mutare" in text:
        return "City of Mutare"
    if "mok/" in text or "kariba" in text:
        return "Municipality of Kariba"
    if "mosad" in text or "skills audit" in text:
        return "Ministry of Skills Audit & Development"
    if "myedvt" in text or "youth empowerment" in text:
        return "Ministry of Youth Empowerment, Development & Vocational Training"
    if "chiirurdc" in text or "chirurdc" in text or "chirumanzu" in text:
        return "Chirumanzu Rural District Council"
    if "hte/" in text or "hwange thermal" in text:
        return "Hwange Electricity Supply Company (HESCO / HTE)"
    if "agra" in text:
        return "Alliance for a Green Revolution in Africa (AGRA)"
    if "eoiunpd" in text or "unpd" in text or "peacekeeping" in text:
        return "United Nations Procurement Division (UNPD)"
    if "lrfp" in text or "unicef" in text:
        return "UNICEF Zimbabwe"
    if "ama/" in text or "agricultural marketing" in text:
        return "Agricultural Marketing Authority (AMA)"
    if "acz/" in text or "airports company" in text:
        return "Airports Company of Zimbabwe (ACZ)"
    if "baz/" in text or "broadcasting authority" in text:
        return "Broadcasting Authority of Zimbabwe (BAZ)"
    if "zetdc" in text or "zesa" in text:
        return "ZETDC / ZESA Holdings"
    if "zinwa" in text or "national water authority" in text:
        return "Zimbabwe National Water Authority (ZINWA)"
    if "potraz" in text or "telecommunications regulatory" in text:
        return "Postal & Telecommunications Regulatory Authority (POTRAZ)"
    if "zimra" in text or "revenue authority" in text:
        return "Zimbabwe Revenue Authority (ZIMRA)"
    if "undp" in text:
        return "United Nations Development Programme (UNDP)"
    if "coh/" in text or "city of harare" in text:
        return "City of Harare"
    if "natpharm" in text:
        return "NatPharm Zimbabwe"
    if "nssa" in text:
        return "National Social Security Authority (NSSA)"
    if "zera" in text:
        return "Zimbabwe Energy Regulatory Authority (ZERA)"
    if "zpc" in text or "power company" in text:
        return "Zimbabwe Power Company (ZPC)"
    if "cmed" in text:
        return "CMED (Private) Limited"
    if "petrotrade" in text:
        return "Petrotrade Zimbabwe"
    if "gmb" in text or "grain marketing" in text:
        return "Grain Marketing Board (GMB)"
    if "nrz" in text or "national railways" in text:
        return "National Railways of Zimbabwe (NRZ)"
    if "telone" in text:
        return "TelOne Zimbabwe"
    if "netone" in text:
        return "NetOne Cellular"
    if "university of zimbabwe" in text or " uz " in text:
        return "University of Zimbabwe"
    if "nust" in text:
        return "National University of Science & Technology (NUST)"
    if "polytechnic" in text:
        return "Polytechnic College (Zimbabwe)"

    # Match "[Entity] invites..."
    m = re.search(r"^([A-Z][A-Za-z0-9\s&,\.\(\)]+?)\s+(?:hereby\s+)?invites", desc)
    if m and len(m.group(1)) > 3 and not m.group(1).lower().startswith("the client"):
        return m.group(1).strip()

    return "Zimbabwe Public Authority / Registered Entity"

def clean_title(cn: str, desc: str) -> str:
    cleaned = desc
    cleaned = re.sub(r'^(?:Re-advertisement(?:\s+of\s+contract\s+number:\s*[^.]+\.)?:\s*)', '', cleaned, flags=re.I).strip()

    prefixes = [
        r'^(?:The\s+Client\s+hereby\s+invites\s+all\s+reputable\s+bidders\s+to\s+bid\s+for\s+(?:the\s+procurement\s+of\s+|the\s+provision\s+of\s+|the\s+supply\s+(?:and|&)\s+delivery\s+of\s+|the\s+)?)(.+)',
        r'^(?:The\s+Municipality\s+(?:hereby\s+)?invites\s+bids\s+from\s+suitably\s+qualified\s+and\s+reputable\s+suppliers\s+for\s+(?:the\s+provision\s+of\s+|the\s+supply\s+(?:and|&)\s+delivery\s+of\s+|the\s+)?)(.+)',
        r'^(?:The\s+client\s+invites\s+suitably\s+qualified\s+bidders\s+for\s+(?:the\s+)?)(.+)',
        r'^(?:The\s+client\s+is\s+inviting\s+bids\s+from\s+reputable\s+bidders\s+for\s+(?:the\s+)?)(.+)',
        r'^(?:The\s+client\s+hereby\s+invites\s+bids\s+from\s+reputable\s+companies\s+to\s+(?:bid\s+for\s+)?(?:the\s+)?)(.+)',
        r'^(?:The\s+client\s+invites\s+(?:prospective\s+)?reputable\s+suppliers\s+for\s+(?:the\s+)?)(.+)',
        r'^(?:The\s+client\s+hereby\s+invites\s+sealed\s+bids\s+from\s+eligible\s+bidders\s+for\s+(?:the\s+)?)(.+)',
        r'^(?:Proposals\s+are\s+hereby\s+invited\s+for\s+(?:the\s+)?)(.+)',
        r'^(?:(?:Tenders|Bids)\s+are\s+(?:hereby\s+|being\s+)?invited\s+(?:from\s+[^for|to]*?)?for\s+(?:the\s+)?(?:procurement\s+of\s+|provision\s+of\s+|supply\s+(?:and|&)\s+delivery\s+of\s+)?)(.+)',
        r'^(?:(?:Tenders|Bids)\s+are\s+(?:hereby\s+|being\s+)?invited\s+to\s+(?:provide\s+)?)(.+)',
        r'^(?:(?:Expressions?\s+of\s+[Ii]nterest|EOI|Quotations?)\s+are\s+hereby\s+invited\s+for\s+(?:the\s+)?)(.+)',
        r'^(?:Interested\s+and\s+qualified\s+companies\s+are\s+invited\s+to\s+(?:bid\s+for\s+)?(?:the\s+)?)(.+)',
        r'^(?:Suitably\s+qualified\s+and\s+experienced\s+bidders\s+are\s+invited\s+to\s+(?:submit\s+bids\s+for\s+)?(?:the\s+)?)(.+)',
    ]
    for pat in prefixes:
        m = re.match(pat, cleaned, re.I)
        if m:
            cleaned = m.group(1).strip()
            break

    if cleaned:
        cleaned = cleaned[0].upper() + cleaned[1:]
    cleaned = re.sub(r'[\.;]+$', '', cleaned).strip()

    if len(cleaned) < 5:
        cleaned = f"Procurement Notice - {cn}"

    return cleaned

def fetch_onlinetenders_page(page_num: int):
    url = BASE_URL if page_num == 1 else f"{BASE_URL}?page={page_num}"
    try:
        resp = requests.get(url, impersonate="chrome120", timeout=12)
        if resp.status_code != 200:
            return page_num, []
        blocks = re.split(r"<div class=[\'\"]tender[\'\"] data-tid=", resp.text)[1:]
        items = []
        for b in blocks:
            tid_m = re.match(r"[\'\"](\d+)[\'\"]", b)
            tid = tid_m.group(1) if tid_m else ""
            if not tid:
                continue

            cn_m = re.search(r"class=[\'\"]tender-cn[^\"]*[\'\"]>(.*?)</div>", b, re.DOTALL)
            cn_raw = re.sub(r"<[^>]+>", " ", cn_m.group(1) if cn_m else "").strip()
            # Strip relative age tags like "2-4 days old", "5 days old", "New", etc.
            cn_clean = re.sub(r"\bNew\b", "", cn_raw, flags=re.I)
            cn_clean = re.sub(r"\b\d+[-–\d]*\s*(?:days?|hours?|weeks?|months?)\s*old\b", "", cn_clean, flags=re.I)
            cn = re.sub(r"\s+", " ", cn_clean).strip()
            # If contract number has a title appended after " - ", extract just the ref number
            if " - " in cn and not cn.startswith("-"):
                parts = cn.split(" - ", 1)
                if len(parts[0].strip()) >= 3:
                    cn = parts[0].strip()

            desc_m = re.search(r"class=[\'\"]tender-desc[^\"]*[\'\"]>(.*?)</div>", b, re.DOTALL)
            desc_raw = re.sub(r"<[^>]+>", " ", desc_m.group(1) if desc_m else "").replace("show more details...", "").strip()
            desc = re.sub(r"\s+", " ", desc_raw).strip()

            cd_m = re.search(r"class=[\'\"]tender-cd[^\"]*[\'\"]>(.*?)</div>", b, re.DOTALL)
            cd_raw = re.sub(r"<[^>]+>", " ", cd_m.group(1) if cd_m else "").strip()

            if not cn or not desc or len(desc) < 5:
                continue

            # Ignore button texts
            if any(cn.lower().startswith(x) for x in ["add to tender", "document collection", "tender assist", "show more"]):
                continue

            items.append({
                "tid": tid,
                "refNo": cn,
                "rawClosing": cd_raw,
                "closingDate": parse_closing_date(cd_raw),
                "description": desc,
            })
        return page_num, items
    except Exception as ex:
        print(f"Error fetching page {page_num}: {ex}")
        return page_num, []

def scrape_all_onlinetenders(max_pages=40):
    print(f"Scraping OnlineTenders Zimbabwe (up to {max_pages} pages)...")
    all_items = []
    with ThreadPoolExecutor(max_workers=8) as executor:
        results = list(executor.map(fetch_onlinetenders_page, range(1, max_pages + 1)))

    for p, items in sorted(results):
        all_items.extend(items)

    print(f"Total raw notices scraped: {len(all_items)}")
    return all_items

def build_supabase_records(items):
    now_iso = datetime.now(timezone.utc).isoformat()
    now_dt = datetime.now(timezone.utc)

    records = []
    seen_ids = set()

    for idx, item in enumerate(items):
        tid = item["tid"]
        ref_no = item["refNo"]
        desc = item["description"]
        closing_date = item["closingDate"]

        # Clean title & infer entity
        title = clean_title(ref_no, desc)
        entity = infer_procuring_entity(ref_no, desc)
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

        # Determine status: active if closingDate is in the future, or closed if expired
        is_live = True
        if closing_date:
            try:
                closing_dt = datetime.fromisoformat(closing_date.replace("Z", "+00:00"))
                is_live = closing_dt >= now_dt
            except:
                is_live = True

        status = "live" if is_live else "closed"

        record_id = f"onlinetenders:{tid}"
        if record_id in seen_ids:
            continue
        seen_ids.add(record_id)

        source_url = BASE_URL
        details_url = f"{BASE_URL}?tid={tid}"
        site_order = idx + 1

        payload = {
            "id": record_id,
            "tenderId": tid,
            "refNo": ref_no,
            "referenceNumber": ref_no,
            "contractNumber": ref_no,
            "title": title,
            "procuringEntity": entity,
            "country": "Zimbabwe",
            "countryCode": "ZW",
            "location": "Zimbabwe",
            "commodityGroup": classification.category,
            "procurementType": "Tender / EOI" if "eoi" in f"{ref_no} {desc}".lower() else "National Competitive Bidding",
            "publishDate": None,
            "closingDate": closing_date,
            "sourcePortal": "OnlineTenders Zimbabwe",
            "portal": "OnlineTenders",
            "sourceUrl": source_url,
            "detailsUrl": details_url,
            "description": desc,
            "siteOrder": site_order,
            "documents": [],
            "lineItems": line_items,
        }
        payload.update(classification_payload(classification))

        details = {
            "title": title,
            "tenderReferenceNumber": ref_no,
            "procuringEntity": entity,
            "description": desc,
            "officialUrl": source_url,
            "closingDate": closing_date,
            "procurementMethod": payload["procurementType"],
            "lineItems": payload["lineItems"],
            "documents": [],
            "siteOrder": site_order,
            "aggregatorNotice": True
        }

        # Stagger scraped_at so that sorting by scraped_at descending matches exact site order
        item_scraped_at = (now_dt - timedelta(seconds=idx)).isoformat()

        records.append({
            "id": record_id,
            "source": "onlinetenders",
            "source_id": tid,
            "country": "ZW",
            "status": status,
            "payload": payload,
            "details": details,
            "scraped_at": item_scraped_at
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

    # Upsert in place so unchanged tenders remain updates instead of being
    # reinserted and emailed as new tenders after every scrape.
    print(f"Uploading {len(records)} clean OnlineTenders records to Supabase in batches...")
    batch_size = 50
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

    print("OnlineTenders Zimbabwe synchronization complete!")

if __name__ == "__main__":
    items = scrape_all_onlinetenders(max_pages=40)
    records = build_supabase_records(items)
    print(f"Built {len(records)} verified tender records.")
    # Show sample
    for r in records[:5]:
        p = r["payload"]
        print(f"ID: {r['id']} | Ref: {p['refNo']} | Entity: {p['procuringEntity']} | Close: {p['closingDate']} | Status: {r['status']}")
        print(f"  Title: {p['title']}")
    upsert_to_supabase(records)
