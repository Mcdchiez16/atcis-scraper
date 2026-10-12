// Isolated PostgreSQL integration tests; never connect to the deployed database.
// Usage: node scripts/test_tender_catalogue.mjs /path/to/@electric-sql/pglite/dist/index.js
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";
const { PGlite } = await import(pathToFileURL(process.argv[2]).href);
const db = new PGlite();
const migration = (name) => readFile(new URL(`../supabase/migrations/${name}.sql`, import.meta.url), "utf8");
let assertions = 0;
function check(value, message) { assert.ok(value, message); assertions++; }
await db.exec(`
  create role anon; create role authenticated; create role service_role bypassrls;
  create schema auth; grant usage on schema public,auth to anon,authenticated,service_role;
  create table auth.users(id uuid primary key);
  create function auth.uid() returns uuid language sql stable as
    $$select nullif(current_setting('request.jwt.claim.sub',true),'')::uuid$$;
  create function auth.role() returns text language sql stable as
    $$select current_setting('request.jwt.claim.role',true)$$;
  create function auth.jwt() returns jsonb language sql stable as $$select '{}'::jsonb$$;
`);
await db.exec(await migration("202609070001_atcis"));
await db.exec(await migration("202609220001_tender_ai_classification_cache"));
// A pre-migration row ensures the backfill really runs.
await db.exec(`insert into public.tenders(id,source,source_id,country,status,payload,scraped_at)
  values('backfill','zppa','backfill','ZM','live','{"title":"Existing notice"}','2026-01-01');`);
const sql = await migration("202610120001_tender_catalogue");
await db.exec(sql);
await db.exec(sql); // Safe migration rerun.
check((await db.query("select catalog_summary->>'title' as title, scraped_at::text as stamp from tenders where id='backfill'")).rows[0].title === "Existing notice", "backfill preserves notice");
check((await db.query("select scraped_at = '2026-01-01'::timestamptz as ok from tenders where id='backfill'")).rows[0].ok, "backfill preserves scrape timestamp");
await db.exec("delete from tenders where id='backfill'");
const users = { ZM: "00000000-0000-0000-0000-000000000001", ZW: "00000000-0000-0000-0000-000000000002", ALL: "00000000-0000-0000-0000-000000000003", inactive: "00000000-0000-0000-0000-000000000004" };
for (const [country, id] of Object.entries(users)) {
  await db.query("insert into auth.users values($1)", [id]);
  await db.query("insert into profiles(id,email,name,role,country,active) values($1,$2,'Test','account_manager',$3,$4)", [id, `${country}@example.invalid`, country === "inactive" ? "ZM" : country, country !== "inactive"]);
}
const future = new Date(Date.now() + 30 * 86400000).toISOString();
async function insert(id, country, fields = {}, status = "live") {
  await db.query("insert into tenders(id,source,source_id,country,status,payload) values($1,$2,$1,$3,$4,$5)", [id, country === "ZW" ? "praz" : "zppa", country, status, JSON.stringify({ title: `Computers ${id}`, referenceNumber: id, publishDate: "2026-10-12T09:00:00+02:00", closingDate: future, sector: "ICT & Software", category: "Computing", classificationConfidence: 96, ...fields })]);
}
await insert("zm-old", "ZM", { publishDate: "2026-10-12T08:00:00+02:00" });
await insert("zm-new", "ZM", { publishDate: "2026-10-12T11:00:00+02:00", documents: [{ secret: "not-a-summary-field" }], lineItems: [{ description: "not-a-summary-field" }], description: "x".repeat(20000) });
await insert("zw-private", "ZW");
await insert("regional", "ALL");
await insert("expired", "ZM", { closingDate: "2000-01-01" });
await insert("closed", "ZM", {}, "closed");
await insert("unknown", "ZM", { closingDate: "invalid", publishDate: "invalid" });
await insert("literal", "ZM", { title: "Discount 100%_safe" });
await insert("soon", "ZM", { closingDate: new Date(Date.now() + 86400000).toISOString() });
async function asUser(user, operation, role = "authenticated") {
  await db.exec("begin");
  try {
    await db.query("select set_config('request.jwt.claim.sub',$1,true),set_config('request.jwt.claim.role',$2,true)", [users[user] ?? "", role]);
    await db.exec(`set local role ${role}`);
    return await operation();
  } finally { await db.exec("rollback"); }
}
async function page(user, args = {}) {
  const names = Object.keys(args);
  return asUser(user, async () => (await db.query(`select public.tender_catalog_page(${names.map((k,i) => `${k} => $${i+1}`).join(",")}) as result`, Object.values(args))).rows[0].result);
}
const zm = await page("ZM");
check(zm.total === 6 && zm.data.length === 6, "count excludes other country, expired and closed rows");
check(zm.data[0].id === "zm-new" && zm.data.at(-1).id === "unknown", "newest timestamp first; unknown dates last");
check(!zm.data.some((row) => row.id === "zw-private"), "RLS excludes ZW for Zambia user");
check((await page("ZM", { p_country: "ZW" })).data.every((row) => row.countryCode === "ALL"), "forged country cannot widen RLS");
check((await page("ZW")).total === 2, "ZW sees ZW and regional only");
check((await page("ALL")).total === 7, "regional user sees all permitted markets");
check(zm.data[0].description.length === 1000 && !JSON.stringify(zm).includes("not-a-summary-field"), "summary omits documents and line items, caps description");
check((await page("ZM", { p_size: 1, p_page: 2 })).data[0].id !== zm.data[0].id, "page 2 advances");
check((await page("ZM", { p_size: 2, p_page: 999 })).page === 3, "out of range clamps to last page");
check((await page("ZM", { p_query: "100%_" })).total === 1, "literal percent and underscore search");
check((await page("ZM", { p_query: "' OR 1=1 --" })).total === 0, "SQL injection remains literal text");
check((await page("ZM", { p_category: "comput", p_focus: true, p_confidence: "TOP" })).total === 6, "sector/category, focus and confidence filters");
check((await page("ZM", { p_portal: "PRAZ" })).total === 0, "portal filter narrows results");
check((await page("ZM", { p_status: "Closing Soon" })).total === 1, "closing soon boundary");
check((await page("ZM", { p_status: "Expired" })).total === 0, "expired excluded from live directory");
for (const args of [{ p_size: 101 }, { p_page: -1 }, { p_sort: "id;drop table tenders" }, { p_query: "x".repeat(301) }]) {
  await assert.rejects(page("ZM", args)); assertions++;
}
await assert.rejects(page("inactive")); assertions++;
await assert.rejects(page("missing")); assertions++;
await assert.rejects(asUser("missing", () => db.query("select public.tender_catalog_page()"), "anon")); assertions++;
await assert.rejects(asUser("ZM", () => db.query("update tenders set payload='{}' where id='zw-private' returning id").then((r) => assert.ok(r.rows.length)))); assertions++;
const dates = (await db.query(`select public.catalog_date('2026-10-12',true) = '2026-10-12T21:59:59.999Z'::timestamptz as end_day,
  public.catalog_date('2026-10-12T11:04:24') = '2026-10-12T09:04:24Z'::timestamptz as local_time,
  public.catalog_date('2026-99-99') is null as invalid`)).rows[0];
check(dates.end_day && dates.local_time && dates.invalid, "local timezone and invalid dates");
await db.query("update tenders set ai_classification=$1,ai_classification_version='tender-sector-v2',ai_classification_refresh_token=gen_random_uuid() where id='zm-old'", [JSON.stringify({ sector: "Healthcare & Medical", subcategory: "Medicine", confidence: 95, evidence: ["Medicine"] })]);
check((await page("ZM", { p_category: "Healthcare" })).total === 1, "saved AI projects without running AI on reads");
await db.exec("update tenders set payload=payload || '{\"title\":\"Changed notice\"}'::jsonb where id='zm-old'");
check((await db.query("select ai_classification is null and ai_classification_refresh_token is null as cleared from tenders where id='zm-old'")).rows[0].cleared, "changed text invalidates classification and in-flight lease");
await db.exec(`update tenders set classification_override='{"sector":"Civil & Infrastructure","subcategory":"Building","confidence":99}' where id='zm-old'`);
check((await page("ZM", { p_category: "Civil" })).data[0].classificationSource === "manual", "manual overrides remain authoritative");
// Synthetic comparison at the observed catalogue size; no network/production timing claims.
await db.exec("truncate tenders");
await db.query(`insert into tenders(id,source,source_id,country,status,payload)
  select 'fixture-'||n,'zppa',n::text,'ZM','live',jsonb_build_object('title','Fixture '||n,
    'publishDate',to_char(now() - n*interval '1 minute','YYYY-MM-DD"T"HH24:MI:SS"Z"'),
    'closingDate',$1::text,'description',repeat('body ',1000),'documents',jsonb_build_array(repeat('document ',500)))
  from generate_series(1,5653) n`, [future]);
await db.exec("analyze tenders");
const start = performance.now();
const bounded = await page("ZM", { p_size: 50 });
const elapsedMs = Math.round(performance.now() - start);
const old = await asUser("ZM", () => db.query("select id,country,source,source_id,payload,scraped_at from tenders where status='live'"));
check(bounded.total === 5653 && bounded.data.length === 50, "5653 notices return exactly 50 summaries with accurate total");
const plan = await asUser("ZM", () => db.query("explain select catalog_summary from tenders where status='live' and (catalog_closes_at is null or catalog_closes_at>=now()) order by catalog_published_at desc nulls last,id limit 50"));
check(JSON.stringify(plan.rows).includes("tenders_catalog_newest_idx"), "newest page uses index under RLS");
console.log(JSON.stringify({ assertions, synthetic: { rows: 5653, returned: bounded.data.length, queryMs: elapsedMs, pagedBytes: Buffer.byteLength(JSON.stringify(bounded)), oldAllRowsBytes: Buffer.byteLength(JSON.stringify(old.rows)) }, plan: plan.rows }, null, 2));
await db.close();
