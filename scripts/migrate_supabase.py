"""One-time, repeatable SQLite -> Supabase import. Never prints credentials.

Usage: python3 scripts/migrate_supabase.py /path/to/temporary-project-api-keys.json
Requires curl (uses the OS certificate store). Existing cloud records are preserved.
"""
import json
import secrets
import sqlite3
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
URL = "https://pqqymbdbkwltzydymild.supabase.co"
keys = json.loads(Path(sys.argv[1]).read_text())
admin_key = next(k["api_key"] for k in keys if k["name"] == "service_role")
public_key = next(k["api_key"] for k in keys if k["name"] == "anon")


def request(path, method="GET", body=None, prefer=None):
    config = ["url = " + json.dumps(URL + path), "request = " + json.dumps(method),
              "header = " + json.dumps("apikey: " + admin_key),
              "header = " + json.dumps("Authorization: Bearer " + admin_key),
              'header = "Content-Type: application/json"']
    if prefer:
        config.append("header = " + json.dumps("Prefer: " + prefer))
    if body is not None:
        config.append("data-binary = " + json.dumps(json.dumps(body)))
    result = subprocess.run(["curl", "--silent", "--show-error", "--max-time", "60", "--config", "-", "--write-out", "\n%{http_code}"],
                            input="\n".join(config), capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError("Supabase connection failed")
    raw, code = result.stdout.rsplit("\n", 1)
    if int(code) >= 400:
        error = json.loads(raw)
        raise RuntimeError(f"Supabase {code}: {error.get('msg') or error.get('message') or error.get('error_code')}")
    return json.loads(raw) if raw else None


def upload(table, rows):
    for offset in range(0, len(rows), 100):
        request("/rest/v1/" + table, "POST", rows[offset:offset + 100], "resolution=ignore-duplicates,return=minimal")
    print(f"Imported/preserved {len(rows)} {table} records", flush=True)


def main():
    db = sqlite3.connect(f"file:{ROOT / 'ZimbabweTenderAPI/tenders.db'}?mode=ro", uri=True)
    db.row_factory = sqlite3.Row
    snapshot = ROOT / "supabase/local-backup.sqlite"
    if not snapshot.exists():
        with sqlite3.connect(snapshot) as backup:
            db.backup(backup)
        snapshot.chmod(0o600)
    db.close()
    db = sqlite3.connect(f"file:{snapshot}?mode=ro", uri=True)
    db.row_factory = sqlite3.Row
    all_tenders = {}
    for table, status in [("LiveTenders", "live"), ("ClosedTenders", "closed")]:
        for row in db.execute('SELECT * FROM "' + table + '" WHERE IsDeleted=0'):
            payload = {k[0].lower() + k[1:]: row[k] for k in row.keys() if k not in ("RowVersion", "CreatedBy", "UpdatedBy", "DeletedBy")}
            for key in ("categoryCodes", "categoryNames"):
                value = payload.get(key) or "[]"
                try:
                    payload[key] = json.loads(value)
                except (ValueError, TypeError):
                    payload[key] = [value]
            source_id = str(row["TenderId"])
            record_id = "praz:" + source_id
            payload.update(id=record_id, countryCode="ZW", sourcePortal="PRAZ e-GP")
            all_tenders[record_id] = dict(id=record_id, source="praz", source_id=source_id,
                country="ZW", status=status, payload=payload)
    upload("tenders", list(all_tenders.values()))

    records = []
    mapping = {"UserTenderAssignments": "assignment", "UserPipelineBoards": "pipeline",
               "TenderChecklistSubmissions": "review", "WorkflowConfigurations": "workflow",
               "RepositoryFolders": "folder", "RepositoryDocuments": "document"}
    for table, kind in mapping.items():
        for row in db.execute('SELECT * FROM "' + table + '"'):
            payload = dict(row)
            record_id = str(payload.get("id") or payload.get("userEmail")).lower() if kind == "pipeline" else str(payload.get("id"))
            country = payload.get("countryCode") or payload.get("country") or "ALL"
            owner = payload.get("assignedToEmail") or payload.get("submittedByEmail") or payload.get("userEmail")
            if kind == "pipeline":
                payload = {"board": json.loads(payload["boardJson"])}
                # Legacy boards have no trusted profile. Preserve for admin review.
                country = "ALL"
            if kind == "review":
                payload["checklist"] = json.loads(payload.pop("checklistJson"))
                payload["approvalSteps"] = json.loads(payload.pop("approvalStepsJson"))
            if kind == "workflow":
                record_id = "default"
                payload = {"steps": json.loads(payload["workflowJson"])}
            if kind == "folder":
                payload["jurisdiction"] = "ALL"
                payload["isCustom"] = bool(payload.get("isCustom"))
            if kind == "assignment":
                payload["addedToPipeline"] = bool(payload.get("addedToPipeline"))
            payload["id"] = record_id
            records.append(dict(kind=kind, id=record_id, country=country, owner_email=owner.lower() if owner else None, payload=payload))
    if not any(r["kind"] == "workflow" for r in records):
        steps = [dict(id="step-hod", name="Head of Department Review", requiredRole="hod", order=1, isRequired=True),
                 dict(id="step-tech", name="Technical Review", requiredRole="technical_review", order=2, isRequired=True),
                 dict(id="step-committee", name="Committee Review", requiredRole="committee", order=3, isRequired=True)]
        records.append(dict(kind="workflow", id="default", country="ALL", owner_email=None, payload={"id": "default", "steps": steps}))
    upload("app_records", records)

    users = request("/auth/v1/admin/users?per_page=1000")["users"]
    for row in db.execute("SELECT * FROM Users WHERE IsActive=1 AND IsDeleted=0"):
        existing = next((u for u in users if u.get("email", "").lower() == row["Email"].lower()), None)
        user = existing or request("/auth/v1/admin/users", "POST", {"email": row["Email"], "password_hash": row["PasswordHash"], "email_confirm": True})
        role_names = [r[0].lower() for r in db.execute("SELECT r.Name FROM Roles r JOIN UserRoles ur ON ur.RoleId=r.Id WHERE ur.UserId=?", (row["Id"],))]
        role = "super_admin" if any(r.replace("_", "").replace(" ", "") == "superadmin" for r in role_names) else "account_manager"
        upload("profiles", [dict(id=user["id"], email=row["Email"].lower(), name=f"{row['FirstName']} {row['LastName']}", role=role, country="ALL", active=(role == "super_admin"))])
        print("Migrated existing user:", row["Email"], flush=True)

    config_path = ROOT / "ZimbabweTenderAPI/appsettings.Supabase.local.json"
    if not config_path.exists():
        password = secrets.token_urlsafe(40)
        scraper = next((u for u in users if u.get("email") == "scraper@atcis.internal"), None)
        if scraper:
            raise RuntimeError("Scraper user exists but its local configuration is missing; do not reset credentials automatically")
        request("/auth/v1/admin/users", "POST", {"email": "scraper@atcis.internal", "password": password,
                "email_confirm": True, "app_metadata": {"atcis_scraper": True}})
        config = {"ScraperOnly": True, "Supabase": {"Url": URL, "PublishableKey": public_key,
                  "ScraperEmail": "scraper@atcis.internal", "ScraperPassword": password},
                  "Scraper": {"Pages": 5, "IntervalMinutes": 10}}
        config_path.write_text(json.dumps(config, indent=2) + "\n")
        config_path.chmod(0o600)
    print("Scraper configuration saved privately. Migration complete.", flush=True)


if __name__ == "__main__":
    main()
