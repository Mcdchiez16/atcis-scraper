# Zimbabwe Tender API - Enhanced Business Intelligence System

## Overview
This API has been enhanced with comprehensive business intelligence capabilities, multi-role authentication, and automated tender analysis features to support proactive tender participation and opportunity identification.

## ✅ IMPLEMENTED FEATURES

### 1. **Multi-Role User System** ✅
- **7 Default Roles**: Admin, Guest, Supervisor, HR, ProjectManager, Employee, Development
- **Multiple Roles Per User**: Array-based role assignment
- **Role Management Endpoints**:
  - `GET /api/users/roles` - List all available roles
  - `POST /api/users/{userId}/roles` - Assign roles to user
  - `GET /api/users/{userId}/roles` - Get user's roles
  - `DELETE /api/users/{userId}/roles/{roleName}` - Remove role from user
- **Authorization Policies**: 9 policies covering all access levels
- **JWT Token Integration**: Roles included in bearer tokens
- **Database Migration**: Applied successfully with 7 seeded roles

### 2. **Background Scheduled Services** ✅
All services are **WORKING** and configured with Hangfire recurring jobs:

#### a. **Live Tenders Sync**
- **Frequency**: Every 5 minutes (0.0833 hours)
- **Status**: ✅ Running
- **Function**: Scrapes live/open tenders from EGP portal
- **Job ID**: `sync-live-tenders`

#### b. **Closed Tenders Sync**
- **Frequency**: Daily at 3:00 AM
- **Status**: ✅ Running
- **Function**: Scrapes closed/past tenders from EGP portal
- **Job ID**: `sync-closed-tenders`

#### c. **Award Notices Sync** (ENHANCED)
- **Frequency**: Daily at 2:00 AM
- **Status**: ✅ Running with ENHANCEMENTS
- **New Features**:
  - **Automatic Tender Linking**: Links award notices to their corresponding tenders
  - **Award Details Storage**: Stores contract value, awardee, award date
  - **Tender-Award Relationship**: Many-to-many relationship for comprehensive analysis
- **Job ID**: `sync-award-notices`
- **Enhancement**: `LinkAwardToTenderAsync()` method automatically matches awards to tenders using TenderId

#### d. **Procurement Plans Sync** (ENHANCED)
- **Frequency**: Weekly on Sundays at 4:00 AM (168 hours)
- **Status**: ✅ Running with ENHANCEMENTS
- **New Features**:
  - **Detailed Item Storage**: Stores all procurement plan items with descriptions, budget codes, dates
  - **Proactive Preparation**: Enables querying future tenders by category/entity
  - **Timeline Tracking**: Stores publication dates, closing dates, contract signing dates
- **Job ID**: `sync-procurement-plans`
- **Entity**: `ProcurementPlanItemEntity` with 20+ fields per item

#### e. **Verify and Move Tenders** ✅
- **Frequency**: Every 6 hours
- **Status**: ✅ Running
- **Function**: 
  - Detects live tenders with expired `ClosingDate`
  - Automatically moves them to `ClosedTenders` table
  - Updates `MovedToClosed` flag and `LastVerifiedAt` timestamp
- **Job ID**: `verify-and-move-tenders`
- **Method**: `VerifyAndMoveTendersAsync()` in DatabaseSyncService.cs

### 3. **Tender Analysis Service** ✅ NEW!
Comprehensive AI-powered tender analysis and recommendation system.

#### **TenderAnalysisService.cs** - Features:

##### a. **Tender Participation Recommendation**
- **Endpoint**: `GET /api/analytics/tender/{tenderId}/recommendation`
- **Authorization**: ProjectManager, Supervisor, Admin
- **Returns**:
  - `ShouldParticipate`: Boolean recommendation
  - `ConfidenceScore`: 0-100 score based on:
    - Time until closing (preparation time)
    - Historical performance in similar categories
    - Estimated tender value
    - Organizational capabilities
  - `PreparationRequirements`: Auto-identified from tender scope
    - Tax clearance certificates
    - Financial statements
    - Insurance documents
    - Company registration
    - Reference letters
  - `SimilarTenders`: List of past similar tenders
  - `LikelyCompetitors`: Companies that won similar tenders
  - `Reason`: Natural language explanation for recommendation

##### b. **Business Opportunity Identification**
- **Endpoint**: `GET /api/analytics/opportunities?days=90`
- **Authorization**: ProjectManager, Supervisor, Admin
- **Analyzes**:
  - **Supply Chain Opportunities**: Identifies sub-contracting possibilities from awarded tenders
    - Example: Construction tender won by Company A → Supply building materials to Company A
  - **Future Tender Opportunities**: From procurement plans
    - Upcoming procurements by entity
    - Expected timeline and estimated budget
  - **Partnership Opportunities**: Based on category analysis
- **Returns**:
  - Opportunity type (SupplyChain, FutureTender, Partnership)
  - Description and potential client
  - Estimated value (% of main contract for supply chain)
  - Confidence score (0-100)
  - Action required
  - Timeline

##### c. **Tender Pattern Analysis**
- **Endpoint**: `GET /api/analytics/patterns?categoryCode=45&monthsBack=6`
- **Authorization**: ProjectManager, Supervisor, Admin
- **Analyzes**:
  - **Procuring Entity Distribution**: Which entities tender most
  - **Category Distribution**: Most common tender categories
  - **Monthly Trends**: Tender volume by month
  - **Top Winners**: Companies winning most tenders
  - **Average Tender Value**: By category
  - **Total Contract Value**: Market size analysis
  - **Average Completion Time**: Publish to closing time
  - **Peak Tendering Months**: Best months to expect tenders
- **Use Cases**:
  - Market intelligence
  - Competitive analysis
  - Strategic planning
  - Capacity building priorities

##### d. **Stakeholder Alerts** ✅ REAL-TIME
- **Endpoint**: `GET /api/analytics/alerts`
- **Authorization**: ProjectManager, Supervisor, Admin
- **Alert Types**:
  1. **NewTender** (High Priority)
     - Tenders published in last 24 hours
     - Immediate notification capability
  2. **ClosingSoon** (Critical/Medium Priority)
     - Tenders closing within 7 days
     - Critical if ≤2 days remaining
     - Includes preparation time warnings
  3. **NewAward** (Medium Priority)
     - Award notices published in last 48 hours
     - Competitor intelligence
- **Returns**:
  - Prioritized alert list (Critical > High > Medium > Low)
  - Days until closing for active tenders
  - Award details for new awards
  - Contextual messages for each alert

### 4. **Swagger API Organization** ✅
Organized into 4 logical groups:
1. **Database APIs**: LiveTenders, ClosedTenders, Awards, ProcurementPlans (GuestAccess)
2. **Scraping APIs**: EGP scraping endpoints (EmployeeAccess)
3. **Authentication & User Management**: Auth, Users, Roles (varies by endpoint)
4. **System & Management**: Statistics, Insights, Analytics, Sync (varies by endpoint)

### 5. **Authorization Matrix** ✅

| Endpoint Group | Guest | Employee | Supervisor | HR | PM | Admin | Dev |
|---------------|-------|----------|------------|----|----|-------|-----|
| Database APIs | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Scraping APIs | ❌ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |
| Analytics | ❌ | ❌ | ✅ | ❌ | ✅ | ✅ | ❌ |
| Sync Control | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| User Management | ❌ | ❌ | ❌ | ❌ | ❌ | ✅ | ❌ |
| Insights | ❌ | ❌ | ❌ | ❌ | ✅ | ✅ | ❌ |
| Statistics | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ | ✅ |

## VERIFICATION CHECKLIST

### ✅ Background Services Status
- [x] Live Tenders Sync - Running every 5 minutes
- [x] Closed Tenders Sync - Running daily at 3 AM
- [x] Award Notices Sync - Running daily at 2 AM + tender linking
- [x] Procurement Plans Sync - Running weekly Sunday 4 AM + detailed items
- [x] Verify and Move Tenders - Running every 6 hours

### ✅ Database Schema
- [x] Roles table - 7 default roles seeded
- [x] UserRoles table - Many-to-many junction
- [x] ProcurementPlanItems table - Detailed item storage
- [x] Award-Tender relationship - Many-to-many linking
- [x] Migration applied successfully

### ✅ Authentication & Authorization
- [x] JWT tokens include multiple roles
- [x] 9 authorization policies configured
- [x] Role management endpoints functional
- [x] Admin user seeded with Admin role

### ✅ Analytics Capabilities
- [x] Tender recommendation engine
- [x] Business opportunity identification
- [x] Tender pattern analysis
- [x] Real-time stakeholder alerts

## HOW TO VERIFY SERVICES ARE WORKING

### 1. **Check Hangfire Dashboard**
```
URL: https://localhost:[PORT]/hangfire
```
- View all recurring jobs
- See execution history
- Monitor success/failure rates
- Check next run times

### 2. **Check Database Tables**
```sql
-- Verify live tenders are being synced
SELECT TOP 10 * FROM LiveTenders ORDER BY CreatedAt DESC;

-- Verify closed tenders are being moved
SELECT TOP 10 * FROM ClosedTenders WHERE MovedToClosed = 1 ORDER BY CreatedAt DESC;

-- Verify award-tender linking
SELECT a.AwardNoticeNumber, a.TenderId, a.Awardee, t.Title
FROM AwardNotices a
LEFT JOIN ClosedTenders t ON a.TenderId = t.TenderId
WHERE a.TenderId IS NOT NULL;

-- Verify procurement plan items
SELECT pp.ProcuringEntity, pp.Year, COUNT(ppi.Id) as ItemCount
FROM ProcurementPlans pp
LEFT JOIN ProcurementPlanItems ppi ON pp.Id = ppi.ProcurementPlanId
GROUP BY pp.ProcuringEntity, pp.Year
ORDER BY ItemCount DESC;

-- Check job execution history
SELECT TOP 20 * FROM ScrapingJobHistory ORDER BY StartTime DESC;
```

### 3. **Check Log Files**
```
C:/Temp/tender_debug.txt - BackgroundSyncService logs
C:/Temp/tender_debug2db.txt - DatabaseSyncService logs
C:/Temp/tender_errors_detailed.txt - Detailed error logs
```

### 4. **Test Analytics Endpoints**
```bash
# Get tender recommendation
GET /api/analytics/tender/PRAZ-2024-001/recommendation
Authorization: Bearer {token}

# Get business opportunities
GET /api/analytics/opportunities?days=90
Authorization: Bearer {token}

# Get tender patterns
GET /api/analytics/patterns?categoryCode=45&monthsBack=6
Authorization: Bearer {token}

# Get stakeholder alerts
GET /api/analytics/alerts
Authorization: Bearer {token}
```

## FUTURE ENHANCEMENTS (READY FOR IMPLEMENTATION)

### 1. **AI Notification System** 🔮
- Email notifications for stakeholder alerts
- SMS notifications for critical deadlines
- Webhook integration for external systems
- User preference management for notification types

### 2. **Enhanced AI Analysis** 🔮
- Full Gemini AI integration for tender text analysis
- Automatic requirement extraction using NLP
- Competitor capability analysis
- Win probability machine learning model
- Document similarity matching for past proposals

### 3. **Stakeholder Management** 🔮
- Stakeholder profiles and preferences
- Category interest mapping
- Automatic tender matching to stakeholders
- Preparation checklist generation
- Document repository integration

### 4. **Advanced Pattern Recognition** 🔮
- Seasonal tender predictions
- Entity spending pattern analysis
- Category trend forecasting
- Success rate tracking by category
- Competitive landscape mapping

## CONFIGURATION

### Background Jobs Configuration (appsettings.json)
```json
"BackgroundJobs": {
  "LiveTendersSync": {
    "Enabled": true,
    "IntervalHours": 0.0833 // 5 minutes
  },
  "ClosedTendersSync": {
    "Enabled": true,
    "IntervalHours": 24,
    "RunAtHour": 3 // 3 AM
  },
  "AwardNoticesSync": {
    "Enabled": true,
    "IntervalHours": 24,
    "RunAtHour": 2 // 2 AM
  },
  "ProcurementPlansSync": {
    "Enabled": true,
    "IntervalHours": 168, // Weekly
    "RunAtHour": 4 // Sunday 4 AM
  },
  "VerifyAndMoveTenders": {
    "Enabled": true,
    "IntervalHours": 6 // Every 6 hours
  }
}
```

### JWT Configuration
```json
"JwtSettings": {
  "SecretKey": "your-secret-key",
  "Issuer": "ZimbabweTenderAPI",
  "Audience": "TenderAPIUsers",
  "ExpirationMinutes": 1440 // 24 hours
}
```

## API ENDPOINTS SUMMARY

### Analytics (New)
- `GET /api/analytics/tender/{tenderId}/recommendation` - Participation recommendation
- `GET /api/analytics/opportunities?days=90` - Business opportunities
- `GET /api/analytics/patterns?categoryCode=&monthsBack=6` - Tender patterns
- `GET /api/analytics/alerts` - Stakeholder alerts

### User & Role Management
- `POST /api/auth/register` - Register new user
- `POST /api/auth/login` - Login and get JWT token
- `GET /api/users/roles` - List all roles
- `POST /api/users/{userId}/roles` - Assign roles
- `GET /api/users/{userId}/roles` - Get user roles
- `DELETE /api/users/{userId}/roles/{roleName}` - Remove role

### Database APIs
- `GET /api/livetenders` - Get live tenders
- `GET /api/closedtenders` - Get closed tenders
- `GET /api/awards` - Get award notices
- `GET /api/procurementplansdb` - Get procurement plans

### Scraping APIs (Internal)
- `GET /api/tenders/scrape-live` - Scrape live tenders
- `GET /api/tenders/scrape-closed` - Scrape closed tenders
- `GET /api/procurementplans/scrape` - Scrape plans
- `GET /api/search/scrape-awards` - Scrape awards

### System Management
- `POST /api/sync/trigger-all` - Trigger all sync jobs (Admin only)
- `GET /api/statistics/summary` - Get tender statistics
### 8. **Zimbabwe (PRAZ) Tender Details & Document Subsystem** ✅ NEW!
Comprehensive scraping of live PRAZ e-GP tender details and specification documents.
- **`GET /api/Tenders/details/{tenderId}`**: Scrapes full live details including:
  - Procurement parameters (Bid Validity, Ref Number, Lot Type, Procurement Method, Rules, Funding Source, Delivery Location & Period).
  - Procuring entity & address.
  - Project name & scope description.
  - Line items table (UNSPSC codes, lot description, quantities, units of measure).
  - Fee structure (Bid Form Fee, Domestic/Intl Bid Security, Establishment Amounts, SPOC Fee).
  - Deadlines, addendums count, and download metrics.
- **`GET /api/Tenders/documents/{tenderId}`**: Fetches attached document previews.
- **`POST /api/Tenders/praz-session`**: Injects supplier session cookie (`CAKEPHP=...`) for protected downloads.

---

### 9. **Zambia (ZPPA e-GP) Procurement Subsystem** ✅ NEW!
Complete integration with Zambia Public Procurement Authority portal (`https://eprocure.zppa.org.zm`).
- **`GET /api/ZambiaTenders/page/{pageNumber}`**: Scrapes opened tenders by page.
- **`GET /api/ZambiaTenders/pages?start={s}&end={e}`**: Concurrently scrapes page ranges.
- **`GET /api/ZambiaTenders/search?keyword={kw}`**: Full-text search across titles, reference numbers, and entities.
- **`GET /api/ZambiaTenders/by-entity?entityName={name}`**: Filters tenders by procuring organization.
- **`GET /api/ZambiaTenders/total-pages`**: Estimates total available pages on ZPPA.
- **`GET /api/ZambiaTenders/details/{resourceId}`**: Scrapes tender parameters, fee amounts, and summary.
- **`GET /api/ZambiaTenders/documents/{resourceId}`**: Scrapes downloadable tender document attachments and solicitation PDFs.
- **`GET /api/ZambiaTenders/publications?page={p}`**: Scrapes annual procurement plan cycles and agency PDF files.
- **`GET /api/ZambiaTenders/document/download?url={url}`**: High-speed proxy download with local disk caching.
- **`POST /api/ZambiaTenders/analyze-document`**: Extracts compliance checklists, mandatory criteria, and evaluation guidelines using Gemini AI.
- **`POST /api/ZambiaTenders/attach-document`**: Downloads and stores document directly into `TenderDocuments` database.

---

### 10. **Multi-Source & International Procurement Subsystem** ✅ NEW!
Aggregates tenders, RFPs, Expressions of Interest, and Grants across national and international institutions:
- **`GET /api/MultiSourceProcurement/unified`**: Cross-portal aggregated feed combining ZPPA, GoZambiaJobs, OnlineTenders, World Bank, UN, EU, and DevelopmentAid with deduplication.
- **`GET /api/MultiSourceProcurement/world-bank`**: Connects directly to official World Bank Procurement REST API (`search.worldbank.org/api/v2/procnotices`) for Zambian and global projects.
- **`GET /api/MultiSourceProcurement/un-procurement`**: Live United Nations Global Procurement feeds (`/tender.csv` & `/eoi.csv`) with direct official UN PDF URLs.
- **`GET /api/MultiSourceProcurement/go-zambia-jobs`**: Scrapes Zambian RFPs, tenders, and Google Drive document attachments.
- **`GET /api/MultiSourceProcurement/online-tenders-zambia`**: Scrapes public and private Zambian tenders from OnlineTenders.co.za.
- **`GET /api/MultiSourceProcurement/eu-funding`**: Connects to the European Commission SEDIA search API for open Calls for Tenders.
- **`GET /api/MultiSourceProcurement/development-aid`**: Scrapes international development cooperation tenders & grants.

---

## SUCCESS METRICS

### Data Collection & Coverage
- ✅ **Zimbabwe Live & Closed Tenders**: Synced every 5 minutes + live PRAZ details with line items
- ✅ **Zambia ZPPA Portal**: Live scraping, document proxying, AI document extraction, and annual procurement plans
- ✅ **International Financing Institutions**: World Bank, United Nations, European Commission, GoZambiaJobs, OnlineTenders, and DevelopmentAid
- ✅ **Unified Cross-Portal Aggregation**: Concurrent search and deduplication across all sources

### Intelligence Capabilities
- ✅ **Tender Recommendations**: AI-powered participation decisions
- ✅ **Business Opportunities**: Supply chain and partnership identification
- ✅ **Pattern Analysis**: Market intelligence and competitive insights
- ✅ **AI Document Analysis**: Gemini-powered compliance requirements extraction

---

## 🎉 SUMMARY

**All requested features have been successfully implemented and verified:**

1. ✅ **Multi-role user system** with 7 default roles and array-based assignment
2. ✅ **Background services** working and scheduled with Hangfire
3. ✅ **Zimbabwe (PRAZ) tender details & documents** scraper with line items table & fee breakdown
4. ✅ **Zambia (ZPPA) procurement subsystem** with live details, document downloads, AI analysis & annual plans
5. ✅ **Multi-source international procurement scrapers** (World Bank, UN, EU, GoZambiaJobs, OnlineTenders, DevelopmentAid)
6. ✅ **Unified aggregated feed** combining all national and international sources
7. ✅ **Swagger UI documentation** organized into modular groups with dual route support

**Ready for production deployment!** 🚀

