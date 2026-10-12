# Tender catalogue rollout

## Deployment order

The code and migration are local. This change does not itself migrate Supabase,
publish a commit, deploy Vercel/Coolify, or enable a scheduler. Apply the database
changes **before** deploying the new frontend; there is intentionally no bulk-read
fallback if the RPC is absent. The workspace has mirrored migration folders; apply
each migration only once, using the files in this backend repository as canonical.

1. Take a database backup/snapshot. Test against a staging copy first.
2. Through an authorised Supabase migration connection or SQL Editor, apply
   `migrations/202609220001_tender_ai_classification_cache.sql` if missing, then
   `migrations/202610120001_tender_catalogue.sql`. The existing base schema/RLS
   migrations must already be installed. Detail views also retain their existing
   prerequisite, `202609080008_tender_detail_cache.sql`.
3. The catalogue migration backfills summaries and builds indexes in a transaction.
   It is additive and preserves notice payloads, details, IDs and scrape timestamps.
   It updates existing rows rather than inserting them, so the insert-only email
   webhook is not fired. Run during a quiet period: backfill/index creation may
   briefly block concurrent scraper writes. Do not remove or bypass RLS.
4. Reclassify legacy notices that lack saved deterministic classification fields.
   From the workspace root, `python3 scripts/reclassify_supabase_tenders.py` reports
   the proposed changes without writing. Once reviewed, run with `--apply` while
   scraper writes are paused to avoid overwriting concurrently refreshed payloads.
   Provide `NEXT_PUBLIC_SUPABASE_URL` and server-only `SUPABASE_SECRET_KEY` securely
   when not using the local environment file. New scraper ingests already save
   deterministic classifications. Unclassified notices remain visible in All Sectors
   as needing review until backfilled; they are excluded from ICT & Health Focus.
5. Deploy the frontend to its existing host (the screenshot shows Vercel) and the
   scraper backend to Coolify. Keep secrets out of Git and all `NEXT_PUBLIC_*`
   variables except the public Supabase URL/publishable key.
6. Verify with active Zambia, Zimbabwe, regional and inactive accounts. Zambia must
   not see Zimbabwe-only notices, even by manipulating query parameters. Check
   portal search, page counts, newest-first ordering, date-only deadlines, and
   opening details. A directory response should contain at most 100 summaries,
   never attachments/full payloads. Default page size is 50.

Database DDL cannot be applied using the REST service key. Use a database migration
connection or the project's SQL Editor; do not expose that credential to the browser.

## Independent classification schedule (optional AI enhancement)

On the **frontend server** configure:

```dotenv
TENDER_CLASSIFICATION_JOB_SECRET=<unique random secret of at least 32 characters>
SUPABASE_SECRET_KEY=<existing Supabase project server secret>
```

Keep the existing AI provider settings if AI classification is wanted. No new AI
provider is required for fast directory reads. Generate a job secret securely, e.g.
`openssl rand -hex 32`, and store it in the host's secret manager.

Configure a trusted scheduler (for example a Coolify scheduled task) to POST to
`https://<frontend-host>/api/internal/tenders/classify` every five minutes with
`Authorization: Bearer <TENDER_CLASSIFICATION_JOB_SECRET>`. Send the secret in the
header, not the URL; avoid logging it or embedding it in committed commands.
Allow up to 90 seconds. Do not schedule this from each browser or from page reads.
The job is disabled when its secret is absent/too short and denies invalid tokens.
Persistent per-notice claims avoid duplicate AI calls across concurrent workers;
failures back off for 15 minutes. Monitor non-2xx job responses and the pending
review count. A large historical backlog requires the deterministic backfill first.

## Source synchronization

Keep `Scraper__Pages=50` and `Scraper__IntervalMinutes=10` on Coolify. The worker
uses the reported PRAZ page total, and invokes ZPPA with `--pages 0` (all pages).
Other source adapters traverse available pagination until the source ends or
repeats results. Source availability, session expiry and source-side changes can
still interrupt a crawl; inspect per-source logs and retry failed sources.
The UI reads already-saved notices and never waits for a scrape to finish.
Do not conclude 235 source results must equal 235 directory results: expired
deadlines, country/portal filters and changes since the screenshot affect the count.

## Local verification

No test connects to Supabase or an AI provider.

Frontend repository:

```sh
node scripts/test-tender-catalogue.mjs
./node_modules/.bin/tsc --noEmit --incremental false --pretty false
```

Backend repository (temporary test dependency, no runtime dependency added):

```sh
TENDER_TEST_DIR=$(mktemp -d /tmp/atcis-catalogue-tests.XXXXXX)
npm install --prefix "$TENDER_TEST_DIR" --no-audit --no-fund @electric-sql/pglite
node scripts/test_tender_catalogue.mjs "$TENDER_TEST_DIR/node_modules/@electric-sql/pglite/dist/index.js"
```

The PostgreSQL test installs the real base RLS and both migrations into an isolated
engine, then checks pagination, filters, country isolation, anonymous/inactive
denial, classification invalidation, compact responses, and index selection.
Its 5,653-row benchmark uses synthetic long payloads; timings and byte reductions
are not production measurements. Measure live p50/p95 latency after deployment.

## Rollback

Roll back the frontend deployment first if needed. Leave the additive database
columns/functions in place; old readers can still use payloads. Disable the
scheduled job if investigating classification. Do not drop tenders, clear saved
notices, disable RLS, or roll back by exposing a service key to the client.
