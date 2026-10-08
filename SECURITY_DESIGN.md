# Multi-Tenant Security & Regional Data Isolation Architecture

**System:** Zimbabwe & Regional Tender Intelligence Platform (v6)  
**Document Version:** 1.0.0  
**Status:** Approved for Implementation  
**Audience:** Backend Engineers, Frontend Engineers, DevOps, Security Auditors  

---

## 1. Executive Summary & Threat Model

The Tender Intelligence Platform aggregates, processes, and serves sensitive procurement data, government tenders, and AI-driven bid competitiveness recommendations across multiple sovereign jurisdictions (primarily **Zimbabwe - PRAZ** and **Zambia - ZPPA**).

### Primary Security Objectives
1. **Strict Multi-Tenant & Sovereign Data Isolation:** Ensure users restricted to one jurisdiction (e.g., Zambian enterprise users) cannot view, query, or infer tender data, bid history, or competitor intelligence from other jurisdictions (e.g., Zimbabwe) unless explicitly authorized with a `REGIONAL` cross-border claim.
2. **Zero-Trust Access Control:** Enforce authorization at the **Database (Row-Level Security)**, **API Layer (Global Query Filters & Authorization Policies)**, and **Frontend (Route Guards & Component-Level Scoping)**.
3. **Integrity of Scraped & Sensitive Intelligence:** Prevent tampering of tender records, bid deadlines, and proprietary AI evaluation scoring.
4. **Comprehensive Auditability:** Maintain an immutable audit log for tender exports, bid analysis requests, and access permission modifications.

```
+-----------------------------------------------------------------------------------+
|                                Client Applications                                 |
|   +---------------------------------------+  +--------------------------------+   |
|   |       Zimbabwe User Session           |  |       Zambia User Session      |   |
|   |     (Claims: scope="ZW", role=PM)     |  |     (Claims: scope="ZM", role) |   |
|   +---------------------------------------+  +--------------------------------+   |
+-----------------------------------------------------------------------------------+
                                         |
                            HTTPS / TLS 1.3 + CSP
                                         v
+-----------------------------------------------------------------------------------+
|                        API Gateway / ASP.NET Core API                             |
|  - JWT Authentication & Claim Validation                                          |
|  - Tenant Scope Middleware (`CountryScope: ZW | ZM | ALL`)                        |
|  - Role-Based Authorization Policies                                              |
|  - Entity Framework Core Global Query Filter Enforcement                          |
+-----------------------------------------------------------------------------------+
                                         |
                        Parameterized Database Queries
                                         v
+-----------------------------------------------------------------------------------+
|                      PostgreSQL / Supabase Database Layer                         |
|  - Row-Level Security (RLS) Policies on `Tenders`, `Awards`, `ProcurementPlans`   |
|  - Schema-Level Isolation & Immutable Audit Logs                                  |
+-----------------------------------------------------------------------------------+
```

---

## 2. Identity & Claims-Based Authentication

### 2.1 JWT Claim Specifications

Every authenticated user session token contains cryptographically signed claims specifying both their functional **Role** and their geographical **CountryScope**.

```json
{
  "sub": "usr_948f2190-7cb1-482a-9f4a-2f47c32b5091",
  "email": "procurement.lead@enterprise.co.zm",
  "name": "Chileshe Mwamba",
  "roles": ["ProjectManager", "Employee"],
  "country_scope": "ZM",
  "allowed_portals": ["ZPPA", "GoZambiaJobs", "WorldBank-ZM"],
  "organization_id": "org_zambia_infra_ltd",
  "iss": "https://auth.tenderintelligence.internal",
  "aud": "https://api.tenderintelligence.internal",
  "exp": 1725289200,
  "iat": 1725260400
}
```

### 2.2 Scope Hierarchy Matrix

| Scope Value | Permitted Access | Restricted Access |
| :--- | :--- | :--- |
| `ZM` | Zambia (ZPPA, RDA, ZESCO, Zambia World Bank/UN tenders) | Blocked from all Zimbabwe tenders, awards & entity data |
| `ZW` | Zimbabwe (PRAZ, ZETDC, NatPharm, City of Harare, REA) | Blocked from all Zambia tenders, awards & entity data |
| `ALL` | Cross-border Regional Access (Zimbabwe, Zambia, UN, World Bank) | Granted only to SuperAdmins & Regional Supervisors |

---

## 3. Backend Implementation: Multi-Layer Isolation

### 3.1 Global Query Filters in Entity Framework Core

To guarantee that developers cannot accidentally leak records across borders via omitted `WHERE` clauses, configure Entity Framework Core with **Global Query Filters**:

```csharp
// Infrastructure/Data/ApplicationDbContext.cs
public class ApplicationDbContext : DbContext
{
    private readonly IUserContextService _userContext;

    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        IUserContextService userContext) : base(options)
    {
        _userContext = userContext;
    }

    public DbSet<TenderEntity> Tenders => Set<TenderEntity>();
    public DbSet<AwardNoticeEntity> AwardNotices => Set<AwardNoticeEntity>();
    public DbSet<ProcurementPlanItemEntity> ProcurementPlans => Set<ProcurementPlanItemEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Enforce Country Scope Isolation at the ORM level
        modelBuilder.Entity<TenderEntity>().HasQueryFilter(t =>
            _userContext.CountryScope == "ALL" || 
            t.CountryCode == _userContext.CountryScope);

        modelBuilder.Entity<AwardNoticeEntity>().HasQueryFilter(a =>
            _userContext.CountryScope == "ALL" || 
            a.CountryCode == _userContext.CountryScope);

        modelBuilder.Entity<ProcurementPlanItemEntity>().HasQueryFilter(p =>
            _userContext.CountryScope == "ALL" || 
            p.CountryCode == _userContext.CountryScope);
    }
}
```

### 3.2 Database-Level Row-Level Security (PostgreSQL)

For direct database access, reporting tools, and Supabase connections, enable PostgreSQL Row-Level Security (RLS):

```sql
-- 1. Enable RLS on core tables
ALTER TABLE tenders ENABLE ROW LEVEL SECURITY;
ALTER TABLE award_notices ENABLE ROW LEVEL SECURITY;
ALTER TABLE procurement_plans ENABLE ROW LEVEL SECURITY;

-- 2. Create RLS Policy for Tenders Table
CREATE POLICY tenant_isolation_policy ON tenders
    FOR ALL
    TO authenticated_role
    USING (
        current_setting('app.current_user_scope', true) = 'ALL' 
        OR country_code = current_setting('app.current_user_scope', true)
    );

-- 3. Set Session Variable upon establishing database connection
-- (Executed by connection pooler / middleware upon checkout)
SET LOCAL app.current_user_scope = 'ZM';
```

---

## 4. Scraper Security & Secret Management

1. **Credential Segregation:**
   - Store external scraper API credentials (World Bank API, UNGM access keys, EGP authentication tokens) inside dedicated environment vaults (**Azure Key Vault** / **AWS Secrets Manager** / **Docker Secrets**).
   - Never commit API keys or session cookies into source code or Git history.
2. **Egress IP Reputation & Rate Limiting:**
   - Outgoing scraping jobs must use isolated proxy rotation with rate-limiting algorithms to avoid IP blocks and comply with robots.txt directives.
3. **Data Sanitization & Injection Prevention:**
   - All text fields scraped from portal HTML pages (Tender Title, Scope Description, Entity Name) must pass through HTML sanitization (`HtmlSanitizer`) before insertion into the database to neutralize stored XSS payloads.

---

## 5. Frontend Security (Next.js & React)

### 5.1 Cookie & Token Storage
- Store JWT access and refresh tokens exclusively in **`HttpOnly`**, **`Secure`**, **`SameSite=Strict`** cookies to prevent extraction via client-side JavaScript (XSS attacks).
- Maintain only non-sensitive display claims (User Name, Initials, Active Scope) in client-side state providers.

### 5.2 Next.js Route Middleware Protection

```typescript
// middleware.ts
import { NextResponse } from 'next/server';
import type { NextRequest } from 'next/server';
import { verifyJwt } from '@/lib/auth/jwt-verifier';

export async function middleware(request: NextRequest) {
  const token = request.cookies.get('auth_token')?.value;

  if (!token) {
    return NextResponse.redirect(new URL('/auth/v1/login', request.url));
  }

  const payload = await verifyJwt(token);
  if (!payload) {
    return NextResponse.redirect(new URL('/auth/v1/login', request.url));
  }

  // Country scope validation for restricted endpoints
  const path = request.nextUrl.pathname;
  if (path.startsWith('/dashboard/zimbabwe') && payload.country_scope === 'ZM') {
    return NextResponse.redirect(new URL('/dashboard/unauthorized', request.url));
  }

  return NextResponse.next();
}

export const config = {
  matcher: ['/dashboard/:path*', '/api/protected/:path*'],
};
```

---

## 6. Audit Logging & Compliance Standards

To maintain non-repudiation and forensic audit readiness, all significant actions must emit a structured JSON audit event to a secure logging sink:

```json
{
  "timestamp": "2026-09-02T07:15:30Z",
  "event_type": "TENDER_DOCUMENT_DOWNLOAD",
  "actor_id": "usr_948f2190-7cb1-482a-9f4a-2f47c32b5091",
  "actor_ip": "102.140.23.11",
  "country_scope": "ZM",
  "resource_id": "ZPPA/RDA/2026/114",
  "action_status": "SUCCESS",
  "user_agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7)..."
}
```

---

## 7. Security Checklist for Deployments

- [x] All default passwords and secret salts removed from configuration files.
- [x] Entity Framework Global Query Filters active on all procurement-related entities.
- [x] PostgreSQL RLS policies applied and verified across staging databases.
- [x] `allowedDevOrigins` restricted strictly to development environments.
- [x] HTTPS TLS 1.3 enforced with HSTS (`max-age=31536000; includeSubDomains; preload`).
- [x] Content Security Policy (CSP) headers enabled on reverse proxies and Next.js headers config.
