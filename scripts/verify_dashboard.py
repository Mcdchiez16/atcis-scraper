"""Verify the built dashboard against the configured Supabase project.
Usage: python3 scripts/verify_dashboard.py /path/to/project-api-keys.json
Uses the existing seeded administrator only if its original password still works.
"""
import base64
import json
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
URL = "https://pqqymbdbkwltzydymild.supabase.co"
keys = json.loads(Path(sys.argv[1]).read_text())
public = next(k["api_key"] for k in keys if k["name"] == "anon")


def request(url, headers=None, body=None):
    config = ["url = " + json.dumps(url)]
    for name, value in (headers or {}).items():
        config.append("header = " + json.dumps(name + ": " + value))
    if body is not None:
        config += ['header = "Content-Type: application/json"', "data-binary = " + json.dumps(json.dumps(body))]
    result = subprocess.run(["curl", "--silent", "--show-error", "--max-time", "90", "--config", "-", "--write-out", "\n%{http_code}"], input="\n".join(config), capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError("HTTP connection failed")
    body, code = result.stdout.rsplit("\n", 1)
    return int(code), body


code, _ = request("http://127.0.0.1:3100/api/assignments")
assert code == 401, "Anonymous API access must be rejected"
print("PASS Anonymous API access rejected", flush=True)
code, _ = request("http://127.0.0.1:3100/api/assignments", {"Cookie": "atcis_auth_token=fake; atcis_user_role=super_admin"})
assert code == 401, "Legacy fake cookies must not authorize requests"
print("PASS Forged legacy administrator cookies rejected", flush=True)

import sqlite3
db = sqlite3.connect(f"file:{ROOT / 'ZimbabweTenderAPI/tenders.db'}?mode=ro", uri=True)
email = db.execute("select Email from Users where IsActive=1 order by Id limit 1").fetchone()[0]
source = (ROOT / "ZimbabweTenderAPI/Data/ApplicationDbContext.cs").read_text()
password = re.search(r'HashPassword\("([^\"]+)"\)', source).group(1)
code, raw = request(URL + "/auth/v1/token?grant_type=password", {"apikey": public}, {"email": email, "password": password})
assert code == 200, "Existing seeded password no longer works; verify login with the user's current credentials"
session = json.loads(raw)
print("PASS Existing administrator can sign in using Supabase", flush=True)
encoded = base64.urlsafe_b64encode(json.dumps(session, separators=(",", ":")).encode()).decode().rstrip("=")
value = "base64-" + encoded
# Supabase SSR chunks long cookies at 3180 characters.
name = "sb-pqqymbdbkwltzydymild-auth-token"
cookie = "; ".join(f"{name}.{i}={value[i*3180:(i+1)*3180]}" for i in range((len(value)+3179)//3180)) if len(value)>3180 else name+"="+value
for path in ["/api/tenders", "/api/assignments", "/api/pipeline?email=" + email,
             "/api/reviews", "/api/templates", "/api/procurement-plans", "/api/records/partner", "/api/scrape"]:
    code, raw = request("http://127.0.0.1:3100" + path, {"Cookie": cookie})
    assert code == 200, f"{path} returned HTTP {code}"
    payload = json.loads(raw)
    assert payload["success"], f"{path} returned failure"
    print("PASS Authenticated", path.split("?")[0], flush=True)
code, raw = request("http://127.0.0.1:3100/dashboard/default", {"Cookie": cookie})
assert code == 200 and "System Administrator" in raw, "Dashboard must render the verified administrator"
print("PASS Production dashboard renders with the verified identity", flush=True)
code, _ = request(URL + "/auth/v1/token?grant_type=password", {"apikey": public}, {"email": email, "password": "intentionally-incorrect-password"})
assert code == 400, "Incorrect passwords must be rejected"
print("PASS Incorrect login password rejected; no demo fallback", flush=True)
print("Dashboard smoke checks passed.", flush=True)
