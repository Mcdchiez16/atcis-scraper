# ATCIS Supabase integration

Project: **Atcis** (`pqqymbdbkwltzydymild`, Europe / Ireland).

The Next.js dashboard uses Supabase Auth and reads/writes Supabase through authenticated Next.js routes. Database row-level policies enforce access even if the browser or a route is bypassed. ASP.NET runs as a scraper-only process when `ScraperOnly=true`; it does not register the legacy controllers, database, authentication, or Hangfire dashboard in that mode.

## Run locally

Start the dashboard from `next-shadcn-admin-dashboard`:

```sh
npm run dev
```

Sign in as `admin@tendersystem.com` using the existing **backend** administrator password. The original bcrypt hash was migrated; it was not replaced with the old frontend's demo password. The email-based role guessing, fake token fallback, and browser account impersonation were removed. Only real Auth users with an active `profiles` entry can enter the application.

Start the scraper from `ZimbabweTenderAPI`:

```sh
dotnet run --no-launch-profile
```

The ignored `appsettings.Supabase.local.json` enables scraper-only mode and configures its restricted identity. The local health endpoint is `http://127.0.0.1:8097/health`. It polls queued requests every 30 seconds and runs scheduled imports every 10 minutes. Stop the previous legacy API process when switching over; changes to the source do not replace a process already running on port 8096.

A bounded scraping check:

```sh
dotnet run --no-launch-profile -- --Scraper:RunOnce=true --Scraper:Pages=1
```

Global scrape requests require a regional administrator. They enter `scrape_requests` as pending and are marked completed or failed by the worker. A queued request is not a completed scrape. The worker must remain running to process requests and scheduled updates.

## Secrets and deployment

The dashboard's ignored `.env.local` contains the Supabase URL, publishable key, and a server-only secret key. Set the same environment variables in the hosting environment:

```dotenv
NEXT_PUBLIC_SUPABASE_URL=https://pqqymbdbkwltzydymild.supabase.co
NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY=your-project-publishable-key
SUPABASE_SECRET_KEY=your-project-secret-or-service-role-key
GEMINI_API_KEY=your-provider-key-if-using-ai
```

`SUPABASE_SECRET_KEY` must never use a `NEXT_PUBLIC_` prefix or be exposed to browser code. It is used only by authenticated super-admin API routes for Auth user management and workload reporting.

The scraper uses an Auth identity whose administrator-controlled `app_metadata.atcis_scraper` flag permits writing tender/plan tables and processing scrape requests. It has no profile and cannot read private application records. It does **not** use the unrestricted service-role key at runtime.

Deploy the scraper using environment variables:

```dotenv
ScraperOnly=true
Supabase__Url=https://pqqymbdbkwltzydymild.supabase.co
Supabase__PublishableKey=your-project-publishable-key
Supabase__ScraperEmail=scraper@atcis.internal
Supabase__ScraperPassword=the-generated-scraper-password
Scraper__Pages=5
Scraper__IntervalMinutes=10
Scraper__ListenUrl=http://127.0.0.1:8097
```

The private scraper JSON is excluded from publish output. Configure deployment secrets explicitly. The account management token (`sbp_…`) is not a project key and is not required by either running application. Rotate the token shared in the conversation from Supabase account settings.

## Accounts and permissions

Regional super administrators manage staff from **Dashboard → Administration → People & Access**. Creating a user adds both the Supabase Auth identity and its application profile in one operation, then shows a generated temporary password once. The same screen supports role, country, department, activation, and password reset changes, plus workload and admin activity summaries.

Supported roles: `account_manager`, `technical_review`, `hod`, `committee`, `country_admin`, `super_admin`. Country is `ZW`, `ZM`, or `ALL`; the administration API reserves `ALL` for `super_admin`. Disabling a profile denies application access. A super administrator cannot remove their own access, and the system prevents disabling the last active super administrator.

HODs and administrators assign tenders to active account managers in the same country. Account managers update their own progress and boards, but only country administrators and super administrators can remove existing pipeline tenders. Country-administrator pipeline deletion is limited to that administrator's country. Only country administrators and super administrators can delete folders, documents, and supplier profiles. Review steps execute atomically, check the authenticated approver's role, reject out-of-order/duplicate approval, and prevent self-approval. Only a regional administrator can change the shared approval workflow. Team pipelines are read-only to other users; HOD assignment can add a task through a controlled database transaction.

## Data and migration

SQL migrations are in `supabase/migrations/` and were applied in filename order. They are intended for a fresh project and are not repeatedly executed against an initialized schema.

`scripts/migrate_supabase.py` imports the backed-up SQLite tender data, existing dashboard assignments/boards/reviews/document metadata, and existing active user password hashes. It uses a private snapshot, batches writes, preserves existing cloud rows on reruns, and reconciles live/closed duplicates by portal tender ID. It does not copy legacy API settings, embedded provider secrets, refresh tokens, or old audit logs into application tables. The original database and `supabase/local-backup.sqlite` retain those records locally.

Existing stored demonstration assignments and document metadata were preserved for review. New empty tables are not automatically filled with demo records. The partners, registrations, renewals, and lessons screens now read their corresponding Supabase records; their original static examples were not imported as real company data. These screens' pre-existing placeholder action buttons still need dedicated editing flows. Record writes are available through the authenticated `/api/records/[kind]` API.

The current repository upload form stores document **metadata**, as it did before. No local document file bytes were present in those records. Actual file uploads/downloads need a Supabase Storage workflow; they are not implied by migrated metadata. WhatsApp and other template-only integrations remain separate features. AI assistance requires a working Gemini key and reports provider errors rather than returning invented tender recommendations.

Tender IDs are now stable `source:source-id` strings. The scraper upserts rather than duplicating notices, retains prior data on source outages, and enriches a bounded set of tender/plan details per cycle. Missing details may require additional cycles. If a portal returns no records, it does not delete previously stored tenders.

## Verification

TypeScript checking, a production Next.js build, and the .NET build were run. The backend retains pre-existing compiler/package warnings.

`scripts/verify_supabase.py` exercises real Supabase login, anonymous access denial, country isolation, role escalation denial, assignment permissions, pipeline ownership, ordered approvals, and restricted scraper writes. Its disposable fixtures are removed after the run. `scripts/verify_dashboard.py` exercises the built dashboard at port 3100, including rejection of forged legacy cookies and the migrated administrator login.

Both verification scripts take the path to a temporary Supabase Management API key-list response, used only for setup/test administration. Never commit that file. Run with network access. The dashboard check uses the original backend seed password only if it still matches; if the administrator changes their password, adapt the check to use privately supplied test credentials.

A real one-page scraper smoke run published **173 tender records and 19 procurement plans** and shut down successfully. Counts refer to records processed in that cycle, including updates, not necessarily newly inserted records.
