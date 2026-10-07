#!/usr/bin/env python3
"""Reclassify existing Supabase tenders without creating new tender rows."""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import sys
import time
import urllib.parse
from collections import Counter
from concurrent.futures import ThreadPoolExecutor, as_completed

from curl_cffi import requests

from tender_classifier import classification_payload, classify_tender


ROOT = pathlib.Path(__file__).resolve().parent.parent
ENV_FILE = ROOT / "next-shadcn-admin-dashboard" / ".env.local"


def load_environment() -> dict[str, str]:
    values: dict[str, str] = {}
    if ENV_FILE.exists():
        for raw_line in ENV_FILE.read_text(encoding="utf-8").splitlines():
            line = raw_line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            key, value = line.split("=", 1)
            values[key.strip()] = value.strip().strip("\"'")
    return values


def fetch_tenders(base_url: str, headers: dict[str, str], limit: int | None) -> list[dict[str, object]]:
    rows: list[dict[str, object]] = []
    page_size = 1000
    for offset in range(0, limit or 1_000_000, page_size):
        remaining = page_size if limit is None else min(page_size, limit - len(rows))
        if remaining <= 0:
            break
        response = requests.get(
            f"{base_url}/rest/v1/tenders?select=id,source,country,status,payload&order=id&offset={offset}&limit={remaining}",
            headers=headers,
            timeout=30,
        )
        response.raise_for_status()
        page = response.json()
        rows.extend(page)
        if len(page) < remaining:
            break
    return rows


def classify_row(row: dict[str, object]) -> tuple[str, dict[str, object], dict[str, object], bool]:
    tender_id = str(row["id"])
    payload = row.get("payload")
    if not isinstance(payload, dict):
        payload = {}
    result = classify_tender(
        title=payload.get("title", ""),
        description=payload.get("description", ""),
        scope=payload.get("scope", ""),
        line_items=payload.get("lineItems") if isinstance(payload.get("lineItems"), list) else None,
    )
    classification = classification_payload(result)
    differences = [key for key, value in classification.items() if payload.get(key) != value]
    needs_update = bool(differences)
    updated = {**payload, **classification}
    summary = {
        "id": tender_id,
        "source": str(row.get("source") or "unknown"),
        "country": str(row.get("country") or ""),
        "status": str(row.get("status") or ""),
        "sector": result.sector,
        "confidence": result.confidence,
        "needs_review": result.needs_review,
        "title": str(payload.get("title") or "")[:120],
        "line_items_preview": (
            payload.get("lineItems")[:1]
            if isinstance(payload.get("lineItems"), list)
            else None
        ),
        "differences": differences,
        "difference_values": {
            key: {"stored": payload.get(key), "expected": classification[key]}
            for key in differences
        },
    }
    return tender_id, updated, summary, needs_update


def patch_tender(base_url: str, headers: dict[str, str], tender_id: str, payload: dict[str, object]) -> None:
    encoded_id = urllib.parse.quote(tender_id, safe="")
    for attempt in range(3):
        try:
            response = requests.patch(
                f"{base_url}/rest/v1/tenders?id=eq.{encoded_id}",
                headers={**headers, "Prefer": "return=representation"},
                data=json.dumps({"payload": payload}),
                timeout=30,
            )
            response.raise_for_status()
            updated_rows = response.json()
            if not isinstance(updated_rows, list) or len(updated_rows) != 1:
                raise RuntimeError(f"Expected one updated tender, got {len(updated_rows) if isinstance(updated_rows, list) else 0}")
            return
        except Exception:
            if attempt == 2:
                raise
            time.sleep(2**attempt)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--apply", action="store_true", help="Write classifications to Supabase")
    parser.add_argument("--limit", type=int, help="Only inspect the first N tenders")
    parser.add_argument("--workers", type=int, default=8)
    parser.add_argument("--examples", type=int, default=0, help="Print sample records that need review")
    parser.add_argument("--diff-examples", type=int, default=0, help="Print sample records needing an update")
    args = parser.parse_args()

    env = load_environment()
    base_url = os.getenv("NEXT_PUBLIC_SUPABASE_URL") or env.get("NEXT_PUBLIC_SUPABASE_URL")
    secret = os.getenv("SUPABASE_SECRET_KEY") or env.get("SUPABASE_SECRET_KEY")
    if not base_url or not secret:
        print("Supabase URL or service key is missing", file=sys.stderr)
        return 1

    headers = {
        "apikey": secret,
        "Authorization": f"Bearer {secret}",
        "Content-Type": "application/json",
    }
    rows = fetch_tenders(base_url.rstrip("/"), headers, args.limit)
    classified = [classify_row(row) for row in rows]
    counts = Counter(summary["sector"] for _, _, summary, _ in classified)
    review_count = sum(bool(summary["needs_review"]) for _, _, summary, _ in classified)
    updates = [(tender_id, payload) for tender_id, payload, _, needs_update in classified if needs_update]
    changed_summaries = [summary for _, _, summary, needs_update in classified if needs_update]
    changed_sources = Counter(summary["source"] for summary in changed_summaries)
    changed_fields = Counter(
        field
        for summary in changed_summaries
        for field in summary["differences"]
    )
    print(
        json.dumps(
            {
                "total": len(classified),
                "sectors": counts,
                "needs_review": review_count,
                "needs_update": len(updates),
                "needs_update_by_source": changed_sources,
                "difference_fields": changed_fields,
            },
            indent=2,
        )
    )
    if args.examples > 0:
        review_examples = [summary for _, _, summary, _ in classified if summary["needs_review"]][: args.examples]
        print(json.dumps({"review_examples": review_examples}, indent=2))
    if args.diff_examples > 0:
        diff_examples = [summary for _, _, summary, needs_update in classified if needs_update][
            : args.diff_examples
        ]
        print(json.dumps({"diff_examples": diff_examples}, indent=2))

    if not args.apply:
        print("Dry run only. Pass --apply to update Supabase.")
        return 0

    failures: list[tuple[str, str]] = []
    with ThreadPoolExecutor(max_workers=max(1, min(args.workers, 16))) as executor:
        futures = {
            executor.submit(patch_tender, base_url.rstrip("/"), headers, tender_id, payload): tender_id
            for tender_id, payload in updates
        }
        for future in as_completed(futures):
            tender_id = futures[future]
            try:
                future.result()
            except Exception as error:
                failures.append((tender_id, str(error)))

    print(json.dumps({"updated": len(updates) - len(failures), "failed": len(failures)}, indent=2))
    if failures:
        for tender_id, error in failures[:10]:
            print(f"{tender_id}: {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
