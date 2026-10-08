# Zimbabwe Tender API - Testing Guide

## Quick Test All New Features

### Prerequisites
1. Application running on `https://localhost:7016`
2. PowerShell or Terminal open
3. Swagger UI accessible at `https://localhost:7016/swagger`

---

## Test 1: Authentication & Role Management

### Step 1: Login as Admin
```powershell
$loginBody = @{
    email = "admin@tenderapi.com"
    password = "Admin@123"
} | ConvertTo-Json

$response = Invoke-RestMethod -Uri "https://localhost:7016/api/auth/login" `
    -Method POST `
    -ContentType "application/json" `
    -Body $loginBody `
    -SkipCertificateCheck

$token = $response.data.token
$headers = @{ "Authorization" = "Bearer $token" }

Write-Host "✅ Logged in successfully!" -ForegroundColor Green
Write-Host "Token: $($token.Substring(0, 50))..." -ForegroundColor Cyan
```

**Expected Result:**
```json
{
  "success": true,
  "message": "Login successful",
  "data": {
    "userId": "1",
    "username": "Administrator",
    "email": "admin@tenderapi.com",
    "token": "eyJhbGc...",
    "roles": ["Admin", "ProjectManager", "Supervisor"]
  }
}
```

### Step 2: List All Users
```powershell
$users = Invoke-RestMethod -Uri "https://localhost:7016/api/auth/users" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host "✅ Found $($users.data.Count) users:" -ForegroundColor Green
$users.data | Format-Table Id, Username, Email, @{Name='Roles';Expression={$_.roles -join ', '}}
```

**Expected Output:**
```
Id Username      Email                    Roles
-- --------      -----                    -----
 1 Administrator admin@tenderapi.com      Admin, ProjectManager, Supervisor
 2 Supervisor    supervisor@tenderapi.com Supervisor, Employee
 3 HRManager     hr@tenderapi.com         HR, Employee
 4 PMUser        pm@tenderapi.com         ProjectManager, Employee
 5 Developer     dev@tenderapi.com        Development, Employee
 6 GuestUser     guest@tenderapi.com      Guest
```

### Step 3: Get Roles for Specific User
```powershell
$userRoles = Invoke-RestMethod -Uri "https://localhost:7016/api/auth/users/2/roles" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host "✅ User 2 roles:" -ForegroundColor Green
$userRoles.data
```

### Step 4: Assign New Role
```powershell
$assignBody = @{
    userId = 5
    role = "ProjectManager"
} | ConvertTo-Json

$result = Invoke-RestMethod -Uri "https://localhost:7016/api/auth/assign-role" `
    -Method POST `
    -Headers $headers `
    -ContentType "application/json" `
    -Body $assignBody `
    -SkipCertificateCheck

Write-Host "✅ $($result.message)" -ForegroundColor Green
```

### Step 5: Remove Role
```powershell
$removeBody = @{
    userId = 5
    role = "Employee"
} | ConvertTo-Json

$result = Invoke-RestMethod -Uri "https://localhost:7016/api/auth/remove-role" `
    -Method DELETE `
    -Headers $headers `
    -ContentType "application/json" `
    -Body $removeBody `
    -SkipCertificateCheck

Write-Host "✅ $($result.message)" -ForegroundColor Green
```

---

## Test 2: Procurement Plans with IDs

### Get Procurement Plans (Paginated)
```powershell
$plans = Invoke-RestMethod -Uri "https://localhost:7016/api/ProcurementPlansDb?page=1&pageSize=5" `
    -Method GET `
    -SkipCertificateCheck

Write-Host "✅ Retrieved $($plans.data.items.Count) procurement plans" -ForegroundColor Green
$plans.data.items | Format-Table Id, ProcuringEntity, Year, TotalItems, TotalEstimatedValue
```

**Expected Output:**
```
Id ProcuringEntity                          Year TotalItems TotalEstimatedValue
-- ----------------                          ---- ---------- -------------------
21 AFC HOLDINGS                             2024         85          1250000.00
21 AFC HOLDINGS                             2025         92          1450000.00
22 AGRICULTURAL AND RURAL DEVELOPMENT...    2024         67           890000.00
22 AGRICULTURAL AND RURAL DEVELOPMENT...    2025         73           920000.00
```

### Get ALL Procurement Plans (No Pagination)
```powershell
$allPlans = Invoke-RestMethod -Uri "https://localhost:7016/api/ProcurementPlansDb/all?year=2024" `
    -Method GET `
    -SkipCertificateCheck

Write-Host "✅ Retrieved ALL $($allPlans.totalCount) plans for 2024" -ForegroundColor Green
Write-Host "Total Estimated Value: $($allPlans.data | Measure-Object -Property totalEstimatedValue -Sum | Select -ExpandProperty Sum)" -ForegroundColor Cyan
```

---

## Test 3: Bulk Data Export (No Pagination)

### Get ALL Live Tenders
```powershell
$allLiveTenders = Invoke-RestMethod -Uri "https://localhost:7016/api/BulkData/live-tenders" `
    -Method GET `
    -SkipCertificateCheck

Write-Host "✅ Retrieved ALL $($allLiveTenders.totalCount) live tenders" -ForegroundColor Green
Write-Host "Latest 5:" -ForegroundColor Cyan
$allLiveTenders.data | Select -First 5 | Format-Table TenderId, Title, ProcuringEntity, PublicationDate
```

### Get ALL Live Tenders for Specific Entity
```powershell
$afcTenders = Invoke-RestMethod -Uri "https://localhost:7016/api/BulkData/live-tenders?entity=AFC" `
    -Method GET `
    -SkipCertificateCheck

Write-Host "✅ Found $($afcTenders.totalCount) tenders for AFC" -ForegroundColor Green
$afcTenders.data | Format-Table TenderId, Title, PublicationDate
```

### Get ALL Closed Tenders
```powershell
$allClosedTenders = Invoke-RestMethod -Uri "https://localhost:7016/api/BulkData/closed-tenders" `
    -Method GET `
    -SkipCertificateCheck

Write-Host "✅ Retrieved ALL $($allClosedTenders.totalCount) closed tenders" -ForegroundColor Green
```

### Get ALL Award Notices
```powershell
$allAwards = Invoke-RestMethod -Uri "https://localhost:7016/api/BulkData/award-notices" `
    -Method GET `
    -SkipCertificateCheck

Write-Host "✅ Retrieved ALL $($allAwards.totalCount) award notices" -ForegroundColor Green
Write-Host "Recent awards:" -ForegroundColor Cyan
$allAwards.data | Select -First 5 | Format-Table AwardNoticeNumber, AwardTitle, Awardee, AwardDate
```

### Get Database Statistics
```powershell
$stats = Invoke-RestMethod -Uri "https://localhost:7016/api/BulkData/statistics" `
    -Method GET `
    -SkipCertificateCheck

Write-Host "`n📊 DATABASE STATISTICS" -ForegroundColor Yellow
Write-Host "======================" -ForegroundColor Yellow
Write-Host "Live Tenders:        $($stats.data.liveTendersCount)" -ForegroundColor Cyan
Write-Host "Closed Tenders:      $($stats.data.closedTendersCount)" -ForegroundColor Cyan
Write-Host "Award Notices:       $($stats.data.awardNoticesCount)" -ForegroundColor Cyan
Write-Host "Procurement Plans:   $($stats.data.procurementPlansCount)" -ForegroundColor Cyan
Write-Host "Total Records:       $($stats.data.totalRecords)" -ForegroundColor Green
Write-Host "Earliest Tender:     $($stats.data.earliestTenderDate)" -ForegroundColor Cyan
Write-Host "Latest Tender:       $($stats.data.latestTenderDate)" -ForegroundColor Cyan
Write-Host "Last Updated:        $($stats.data.lastUpdated)" -ForegroundColor Cyan
```

---

## Test 4: Procurement Plan Details (with Items)

### Get Plan with All Items
```powershell
$planDetails = Invoke-RestMethod -Uri "https://localhost:7016/api/ProcurementPlansDb/42" `
    -Method GET `
    -SkipCertificateCheck

Write-Host "✅ Retrieved plan: $($planDetails.data.title)" -ForegroundColor Green
Write-Host "Entity: $($planDetails.data.procuringEntity)" -ForegroundColor Cyan
Write-Host "Year: $($planDetails.data.year)" -ForegroundColor Cyan
Write-Host "Total Items: $($planDetails.data.items.Count)" -ForegroundColor Cyan
Write-Host "`nFirst 5 items:" -ForegroundColor Yellow
$planDetails.data.items | Select -First 5 | Format-Table ItemId, RefNo, Description, ProcurementMethod
```

**Expected Output:**
```
ItemId RefNo  Description                    ProcurementMethod
------ -----  -----------                    -----------------
267489 AFC 01 Laptops                        Competitive Bidding Method
267490 AFC 02 Motor Vehicle and equipment... Competitive Bidding Method
267491 AFC A02 Motor Vehicle Fuels          Direct Procurement Method
```

### Get Plan Items Only
```powershell
$items = Invoke-RestMethod -Uri "https://localhost:7016/api/ProcurementPlansDb/42/items" `
    -Method GET `
    -SkipCertificateCheck

Write-Host "✅ Retrieved $($items.data.Count) items" -ForegroundColor Green
$items.data | Format-Table ItemId, RefNo, Description, TenderPublicationDate, BidClosingDate
```

---

## Test 5: AI Insights (Requires ProjectManager Role)

### Quick Insights
```powershell
$insights = Invoke-RestMethod -Uri "https://localhost:7016/api/Insights/quick?q=IT%20tenders&includeAnalysis=true" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host "✅ AI Analysis Complete" -ForegroundColor Green
Write-Host $insights.data.analysis
```

### Company Profile Analysis
```powershell
$companyProfile = "Axis Solutions as an IT company specializing in software development and cloud services"
$encodedProfile = [System.Web.HttpUtility]::UrlEncode($companyProfile)

$companyInsights = Invoke-RestMethod -Uri "https://localhost:7016/api/Insights/company/$encodedProfile?pages=2" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host "✅ Company Insights Generated" -ForegroundColor Green
Write-Host $companyInsights.data.insights
```

### Custom Analysis
```powershell
$customAnalysis = Invoke-RestMethod -Uri "https://localhost:7016/api/Insights/analyze-custom?q=Axis%20Solutions%20IT%20tenders&maxTenders=10&includeRecommendations=true" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host "✅ Custom Analysis Complete" -ForegroundColor Green
Write-Host "`nAnalysis:" -ForegroundColor Yellow
Write-Host $customAnalysis.data.analysis
Write-Host "`nRecommendations:" -ForegroundColor Yellow
Write-Host $customAnalysis.data.recommendations
```

---

## Test 6: Settings Management (Admin Only)

### Get All Settings
```powershell
$settings = Invoke-RestMethod -Uri "https://localhost:7016/api/Settings" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host "✅ Retrieved $($settings.data.Count) settings" -ForegroundColor Green
$settings.data | Group-Object Category | Format-Table Name, Count
```

### Get Specific Setting
```powershell
$liveTenderInterval = Invoke-RestMethod -Uri "https://localhost:7016/api/Settings/key/BackgroundJobs:LiveTendersSync:IntervalHours" `
    -Method GET `
    -Headers $headers `
    -SkipCertificateCheck

Write-Host "✅ Live Tender Sync Interval: $($liveTenderInterval.data.value) hours" -ForegroundColor Green
```

### Update Setting
```powershell
$updateBody = @{
    key = "BackgroundJobs:LiveTendersSync:IntervalHours"
    value = "0.0833"
    description = "Sync every 5 minutes (updated via API)"
} | ConvertTo-Json

$updateResult = Invoke-RestMethod -Uri "https://localhost:7016/api/Settings/key/BackgroundJobs:LiveTendersSync:IntervalHours" `
    -Method PUT `
    -Headers $headers `
    -ContentType "application/json" `
    -Body $updateBody `
    -SkipCertificateCheck

Write-Host "✅ $($updateResult.message)" -ForegroundColor Green
```

---

## Complete Test Script

### Run All Tests at Once
```powershell
# Save this as test-all.ps1

Write-Host "`n🚀 ZIMBABWE TENDER API - COMPREHENSIVE TEST`n" -ForegroundColor Yellow

# 1. LOGIN
Write-Host "1️⃣ Testing Authentication..." -ForegroundColor Cyan
$loginBody = @{ email = "admin@tenderapi.com"; password = "Admin@123" } | ConvertTo-Json
$response = Invoke-RestMethod -Uri "https://localhost:7016/api/auth/login" -Method POST -ContentType "application/json" -Body $loginBody -SkipCertificateCheck
$token = $response.data.token
$headers = @{ "Authorization" = "Bearer $token" }
Write-Host "   ✅ Login successful`n" -ForegroundColor Green

# 2. USERS
Write-Host "2️⃣ Testing User Management..." -ForegroundColor Cyan
$users = Invoke-RestMethod -Uri "https://localhost:7016/api/auth/users" -Method GET -Headers $headers -SkipCertificateCheck
Write-Host "   ✅ Found $($users.data.Count) users`n" -ForegroundColor Green

# 3. PROCUREMENT PLANS
Write-Host "3️⃣ Testing Procurement Plans..." -ForegroundColor Cyan
$plans = Invoke-RestMethod -Uri "https://localhost:7016/api/ProcurementPlansDb?page=1&pageSize=5" -Method GET -SkipCertificateCheck
Write-Host "   ✅ Retrieved $($plans.data.items.Count) plans (first IDs: $($plans.data.items[0].id), $($plans.data.items[1].id))`n" -ForegroundColor Green

# 4. BULK DATA
Write-Host "4️⃣ Testing Bulk Data Export..." -ForegroundColor Cyan
$stats = Invoke-RestMethod -Uri "https://localhost:7016/api/BulkData/statistics" -Method GET -SkipCertificateCheck
Write-Host "   ✅ Database has $($stats.data.totalRecords) total records" -ForegroundColor Green
Write-Host "      Live: $($stats.data.liveTendersCount) | Closed: $($stats.data.closedTendersCount) | Awards: $($stats.data.awardNoticesCount) | Plans: $($stats.data.procurementPlansCount)`n" -ForegroundColor Gray

# 5. INSIGHTS
Write-Host "5️⃣ Testing AI Insights..." -ForegroundColor Cyan
$insights = Invoke-RestMethod -Uri "https://localhost:7016/api/Insights/quick?q=IT%20tenders&includeAnalysis=true" -Method GET -Headers $headers -SkipCertificateCheck
Write-Host "   ✅ AI analysis generated ($($insights.data.analysis.Length) characters)`n" -ForegroundColor Green

# 6. SETTINGS
Write-Host "6️⃣ Testing Settings Management..." -ForegroundColor Cyan
$settings = Invoke-RestMethod -Uri "https://localhost:7016/api/Settings" -Method GET -Headers $headers -SkipCertificateCheck
Write-Host "   ✅ Retrieved $($settings.data.Count) settings`n" -ForegroundColor Green

Write-Host "`n🎉 ALL TESTS PASSED!`n" -ForegroundColor Green
```

---

## Expected Results Summary

| Test | Endpoint | Expected Result |
|------|----------|-----------------|
| Login | POST /api/auth/login | 200 OK, returns token and user info |
| List Users | GET /api/auth/users | 200 OK, returns 6 default users |
| Get User Roles | GET /api/auth/users/2/roles | 200 OK, returns ["Supervisor", "Employee"] |
| Assign Role | POST /api/auth/assign-role | 200 OK, role assigned successfully |
| Remove Role | DELETE /api/auth/remove-role | 200 OK, role removed successfully |
| Get Plans | GET /api/ProcurementPlansDb | 200 OK, includes ID, totalItems, totalEstimatedValue |
| Get All Plans | GET /api/ProcurementPlansDb/all | 200 OK, all plans without pagination |
| Get All Live Tenders | GET /api/BulkData/live-tenders | 200 OK, complete dataset |
| Get All Closed Tenders | GET /api/BulkData/closed-tenders | 200 OK, complete dataset |
| Get All Awards | GET /api/BulkData/award-notices | 200 OK, complete dataset |
| Get Statistics | GET /api/BulkData/statistics | 200 OK, aggregated counts |
| Get Plan Details | GET /api/ProcurementPlansDb/{id} | 200 OK, includes items array |
| Get Plan Items | GET /api/ProcurementPlansDb/{id}/items | 200 OK, array of items |
| Quick Insights | GET /api/Insights/quick | 200 OK (with token), AI analysis |
| Company Insights | GET /api/Insights/company/{profile} | 200 OK (with token), recommendations |
| Custom Analysis | GET /api/Insights/analyze-custom | 200 OK (with token), analysis + recommendations |
| Get Settings | GET /api/Settings | 200 OK (admin token), 31 settings |
| Get Setting | GET /api/Settings/key/{key} | 200 OK (admin token), setting value |
| Update Setting | PUT /api/Settings/key/{key} | 200 OK (admin token), setting updated |

---

## Troubleshooting

### Error: "Cannot invoke method. Method invocation is supported only on core types in this language mode."
**Fix:** Run PowerShell as Administrator or use `-SkipCertificateCheck` flag

### Error: "401 Unauthorized"
**Fix:** 
1. Make sure you're logged in: `POST /api/auth/login`
2. Copy the token from response
3. Add to headers: `Authorization: Bearer {token}`

### Error: "403 Forbidden"
**Fix:** Use admin account for admin endpoints:
- Email: `admin@tenderapi.com`
- Password: `Admin@123`

### Error: "The SSL connection could not be established"
**Fix:** Add `-SkipCertificateCheck` to all PowerShell requests (dev environment only)

### No Data Returned
**Fix:** Check if background jobs have run:
1. Access Hangfire dashboard: `https://localhost:7016/hangfire`
2. Check "Recurring Jobs" tab
3. Trigger jobs manually if needed
