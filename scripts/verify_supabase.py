"""Integration checks using disposable accounts, removed in finally.
Usage: python3 scripts/verify_supabase.py /path/to/project-api-keys.json
"""
import json
import secrets
import subprocess
import sys
from pathlib import Path
from urllib.parse import quote

URL = "https://pqqymbdbkwltzydymild.supabase.co"
keys = json.loads(Path(sys.argv[1]).read_text())
ADMIN = next(k["api_key"] for k in keys if k["name"] == "service_role")
PUBLIC = next(k["api_key"] for k in keys if k["name"] == "anon")
RUN = "verify-" + secrets.token_hex(5)
created = []
checks = 0


def call(path, token=ADMIN, method="GET", body=None):
    config = ["url = " + json.dumps(URL + path), "request = " + json.dumps(method),
              "header = " + json.dumps("apikey: " + (ADMIN if token == ADMIN else PUBLIC)),
              "header = " + json.dumps("Authorization: Bearer " + token),
              'header = "Content-Type: application/json"', 'header = "Prefer: return=representation"']
    if body is not None:
        config.append("data-binary = " + json.dumps(json.dumps(body)))
    retry = ["--retry", "2", "--retry-delay", "1", "--retry-all-errors"] if method == "GET" or "grant_type=password" in path else []
    result = subprocess.run(["curl", *retry, "--silent", "--show-error", "--max-time", "45", "--config", "-", "--write-out", "\n%{http_code}"], input="\n".join(config), capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError("Supabase network request failed")
    raw, code = result.stdout.rsplit("\n", 1)
    return int(code), json.loads(raw) if raw else None


def check(condition, message):
    global checks
    assert condition, message
    checks += 1
    print("PASS", message, flush=True)


def account(role, country):
    email = (RUN + "." + role + country + "@example.invalid").lower()
    password = secrets.token_urlsafe(30)
    code, user = call("/auth/v1/admin/users", method="POST", body={"email": email, "password": password, "email_confirm": True})
    assert code < 300, "Fixture user creation failed"
    created.append(user["id"])
    code, _ = call("/rest/v1/profiles", method="POST", body={"id": user["id"], "email": email, "name": "Verification fixture", "role": role, "country": country})
    assert code < 300, "Fixture profile creation failed"
    code, session = call("/auth/v1/token?grant_type=password", PUBLIC, "POST", {"email": email, "password": password})
    assert code == 200, "Fixture login failed"
    return session["access_token"], email, user["id"]


try:
    code, _ = call("/rest/v1/tenders?select=id&limit=1", PUBLIC)
    check(code in (401, 403), "Anonymous users cannot read tenders")
    am, email, uid = account("account_manager", "ZW")
    hod, _, _ = account("hod", "ZW")
    zm, zm_email, _ = account("account_manager", "ZM")
    tech, _, _ = account("technical_review", "ZW")
    code, rows = call("/rest/v1/tenders?country=eq.ZW&select=id&limit=1", am)
    check(code == 200 and len(rows) > 0, "Verified user can read imported Zimbabwe tenders")
    code, rows = call("/rest/v1/tenders?country=eq.ZW&select=id&limit=1", zm)
    check(code == 200 and rows == [], "Zambia user cannot read Zimbabwe-only tenders")
    code, _ = call("/rest/v1/profiles?id=eq." + uid, am, "PATCH", {"role": "super_admin"})
    check(code in (401, 403), "User cannot promote their own role")
    code, _ = call("/rest/v1/tenders", am, "POST", {"id": RUN, "source": "test", "source_id": RUN, "country": "ZW", "status": "live", "payload": {}})
    check(code in (401, 403), "Application user cannot write scraper-owned tenders")
    assignment = {"id": RUN, "country": "ZW", "assignedToEmail": email, "status": "Assigned", "tenderTitle": "Verification fixture", "assignedToName": "Verification fixture", "addedToPipeline": True, "pipelineTaskId": RUN}
    code, _ = call("/rest/v1/rpc/save_app_record", hod, "POST", {"p_kind": "assignment", "p_id": RUN, "p_payload": assignment})
    check(code == 200, "HOD can assign a tender to an active account manager")
    code, stored_pipelines = call("/rest/v1/app_records?kind=eq.pipeline&select=id,country,owner_email,payload", ADMIN)
    stored_pipeline = next((row for row in stored_pipelines if row["id"] == email), None)
    check(stored_pipeline is not None and stored_pipeline["payload"]["board"]["new"][0]["id"] == RUN, "Assignment transaction creates the pipeline task")
    code, pipelines = call("/rest/v1/app_records?kind=eq.pipeline&select=id,payload", am)
    pipeline = next((row for row in pipelines if row["id"] == email), None)
    check(code == 200 and pipeline is not None, "Account manager can read the assigned pipeline task")
    code, data = call("/rest/v1/rpc/save_app_record", am, "POST", {"p_kind": "assignment", "p_id": RUN, "p_payload": {**assignment, "assignedToEmail": zm_email, "status": "In Progress"}})
    check(code == 200 and data["assignedToEmail"] == email and data["status"] == "In Progress", "AM can update progress but cannot reassign ownership")
    code, rows = call("/rest/v1/app_records?kind=eq.assignment&id=eq." + RUN, zm)
    check(code == 200 and rows == [], "Cross-country private assignment is hidden")
    code, _ = call("/rest/v1/rpc/save_app_record", am, "POST", {"p_kind": "pipeline", "p_id": zm_email, "p_payload": {"board": {}}})
    check(code in (401, 403), "User cannot overwrite another user's pipeline")
    code, _ = call("/rest/v1/rpc/save_app_record", am, "POST", {"p_kind": "pipeline", "p_id": email, "p_payload": {"board": {"new": []}}})
    check(code == 200, "Owner can save their pipeline")
    code, _ = call("/rest/v1/rpc/review_action", am, "POST", {"p_action": "submit_checklist", "p_id": RUN, "p_payload": {"countryCode": "ZW", "tenderRef": RUN, "checklist": []}})
    check(code == 200, "Checklist submission starts the stored workflow")
    code, _ = call("/rest/v1/rpc/review_action", tech, "POST", {"p_action": "approve_step", "p_id": RUN, "p_step_id": "step-tech"})
    check(code >= 400, "Out-of-order approval is rejected")
    code, _ = call("/rest/v1/rpc/review_action", am, "POST", {"p_action": "approve_step", "p_id": RUN, "p_step_id": "step-hod"})
    check(code >= 400, "Submitter cannot approve their own review")
    code, data = call("/rest/v1/rpc/review_action", hod, "POST", {"p_action": "approve_step", "p_id": RUN, "p_step_id": "step-hod"})
    check(code == 200 and data["currentStepIndex"] == 1, "Authorized approval advances exactly one step")
    code, _ = call("/rest/v1/rpc/review_action", hod, "POST", {"p_action": "approve_step", "p_id": RUN, "p_step_id": "step-hod"})
    check(code >= 400, "Duplicate approval is rejected")

    config = json.loads((Path(__file__).resolve().parents[1] / "ZimbabweTenderAPI/appsettings.Supabase.local.json").read_text())["Supabase"]
    code, session = call("/auth/v1/token?grant_type=password", PUBLIC, "POST", {"email": config["ScraperEmail"], "password": config["ScraperPassword"]})
    check(code == 200, "Dedicated scraper can authenticate")
    token = session["access_token"]
    code, data = call("/rest/v1/app_records?limit=1", token)
    check(code == 200 and data == [], "Scraper cannot read private application records")
    code, _ = call("/rest/v1/tenders", token, "POST", {"id": RUN, "source": "test", "source_id": RUN, "country": "ZW", "status": "closed", "payload": {"title": "Verification fixture"}})
    check(code == 201, "Scraper can publish a tender")
    print(f"All {checks} Supabase integration checks passed.", flush=True)
finally:
    # Delete only identifiers created by this run.
    call("/rest/v1/tenders?id=eq." + RUN, method="DELETE")
    call("/rest/v1/app_records?id=eq." + RUN, method="DELETE")
    for uid in created:
        code, user = call("/auth/v1/admin/users/" + uid)
        if code == 200:
            call("/rest/v1/app_records?kind=eq.pipeline&id=eq." + quote(user["email"], safe=""), method="DELETE")
        call("/auth/v1/admin/users/" + uid, method="DELETE")
