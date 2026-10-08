# Database-Driven Configuration System

## Overview
The system now uses a database-first configuration approach, allowing administrators to modify critical settings at runtime without redeployment. Settings are stored in the `SystemSettings` table with automatic fallback to `appsettings.json` if the database is unavailable.

---

## 🎯 Key Features

1. **Database-First with Fallback**: Reads from database → falls back to appsettings.json → uses default value
2. **Admin-Only Access**: All settings endpoints require `AdminOnly` policy
3. **Protected Settings**: Critical settings like `JWT:SecretKey` cannot be deleted
4. **Grouped by Category**: Settings organized into 7 categories
5. **Audit Trail**: All changes tracked with timestamps and user info
6. **Hot Reload**: Background jobs read from database on application restart

---

## 📊 Available Settings (31 Total)

### Background Jobs (13 settings)
| Key | Default | Description |
|-----|---------|-------------|
| `BackgroundJobs:LiveTendersSync:Enabled` | `true` | Enable/disable live tenders sync |
| `BackgroundJobs:LiveTendersSync:IntervalHours` | `0.0833` | Sync interval (5 minutes) |
| `BackgroundJobs:ClosedTendersSync:Enabled` | `true` | Enable/disable closed tenders sync |
| `BackgroundJobs:ClosedTendersSync:IntervalHours` | `24` | Daily sync interval |
| `BackgroundJobs:ClosedTendersSync:RunAtHour` | `3` | Run at 3 AM |
| `BackgroundJobs:AwardNoticesSync:Enabled` | `true` | Enable/disable award notices sync |
| `BackgroundJobs:AwardNoticesSync:IntervalHours` | `24` | Daily sync interval |
| `BackgroundJobs:AwardNoticesSync:RunAtHour` | `2` | Run at 2 AM |
| `BackgroundJobs:ProcurementPlansSync:Enabled` | `true` | Enable/disable procurement plans sync |
| `BackgroundJobs:ProcurementPlansSync:IntervalHours` | `168` | Weekly (7 days) |
| `BackgroundJobs:ProcurementPlansSync:RunAtHour` | `4` | Run at 4 AM Sunday |
| `BackgroundJobs:VerifyAndMoveTenders:Enabled` | `true` | Enable/disable verify & move |
| `BackgroundJobs:VerifyAndMoveTenders:IntervalHours` | `6` | Every 6 hours |

### JWT Settings (2 settings)
| Key | Default | Description |
|-----|---------|-------------|
| `JWT:ExpirationMinutes` | `1440` | Token expiration (24 hours) |
| `JWT:RefreshTokenExpirationDays` | `7` | Refresh token lifetime |

### Gemini AI (4 settings)
| Key | Default | Description |
|-----|---------|-------------|
| `Gemini:Model` | `gemini-2.0-flash-exp` | AI model to use |
| `Gemini:Temperature` | `0.7` | Response randomness (0.0-1.0) |
| `Gemini:MaxTokens` | `8000` | Maximum response length |
| `Gemini:Enabled` | `true` | Enable/disable AI features |

### Scraping (4 settings)
| Key | Default | Description |
|-----|---------|-------------|
| `Scraping:BatchSize` | `100` | Items per batch |
| `Scraping:TimeoutSeconds` | `30` | HTTP timeout |
| `Scraping:RetryAttempts` | `3` | Retry failed requests |
| `Scraping:DelayBetweenRequestsMs` | `500` | Delay between requests |

### Pagination (2 settings)
| Key | Default | Description |
|-----|---------|-------------|
| `Pagination:DefaultPageSize` | `20` | Default items per page |
| `Pagination:MaxPageSize` | `100` | Maximum allowed page size |

### Analytics (3 settings)
| Key | Default | Description |
|-----|---------|-------------|
| `Analytics:DefaultDaysBack` | `90` | Default lookback period |
| `Analytics:MinConfidenceScore` | `60` | Minimum recommendation confidence |
| `Analytics:MaxOpportunities` | `50` | Max opportunities to return |

### Logging (3 settings)
| Key | Default | Description |
|-----|---------|-------------|
| `Logging:EnableFileLogging` | `true` | Enable file logging |
| `Logging:LogPath` | `C:/Temp` | Log file directory |
| `Logging:RetentionDays` | `30` | Days to keep logs |

---

## 🔐 API Endpoints (Admin Only)

### 1. Get All Settings
```http
GET /api/settings
Authorization: Bearer <admin-token>
```

**Response:**
```json
{
  "BackgroundJobs": [
    {
      "id": 1,
      "key": "BackgroundJobs:LiveTendersSync:Enabled",
      "value": "true",
      "description": "Enable/disable live tenders sync job",
      "category": "BackgroundJobs",
      "createdAt": "2026-01-27T05:17:55.7585071Z",
      "createdBy": "System"
    }
  ],
  "JWT": [...],
  "Gemini": [...]
}
```

---

### 2. Get Settings by Category
```http
GET /api/settings/category/BackgroundJobs
Authorization: Bearer <admin-token>
```

---

### 3. Get Specific Setting
```http
GET /api/settings/key/BackgroundJobs:LiveTendersSync:IntervalHours
Authorization: Bearer <admin-token>
```

**Response:**
```json
{
  "id": 2,
  "key": "BackgroundJobs:LiveTendersSync:IntervalHours",
  "value": "0.0833",
  "description": "Live tenders sync interval in hours (0.0833 = 5 minutes)",
  "category": "BackgroundJobs"
}
```

---

### 4. Update Single Setting
```http
PUT /api/settings/key/BackgroundJobs:LiveTendersSync:IntervalHours
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "value": "0.1"
}
```

**Response:**
```json
{
  "id": 2,
  "key": "BackgroundJobs:LiveTendersSync:IntervalHours",
  "value": "0.1",
  "description": "Live tenders sync interval in hours (0.0833 = 5 minutes)",
  "category": "BackgroundJobs",
  "updatedAt": "2026-01-27T06:30:00Z",
  "updatedBy": "admin@example.com"
}
```

---

### 5. Bulk Update Settings
```http
PUT /api/settings/bulk
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "updates": [
    {
      "key": "BackgroundJobs:LiveTendersSync:IntervalHours",
      "value": "0.0833"
    },
    {
      "key": "JWT:ExpirationMinutes",
      "value": "2880"
    },
    {
      "key": "Gemini:Temperature",
      "value": "0.8"
    }
  ]
}
```

**Response:**
```json
{
  "message": "3 settings updated successfully",
  "updatedCount": 3,
  "failedCount": 0,
  "updatedSettings": [...]
}
```

---

### 6. Create Custom Setting
```http
POST /api/settings
Authorization: Bearer <admin-token>
Content-Type: application/json

{
  "key": "CustomFeature:Timeout",
  "value": "120",
  "description": "Custom feature timeout in seconds",
  "category": "Custom"
}
```

---

### 7. Delete Custom Setting
```http
DELETE /api/settings/key/CustomFeature:Timeout
Authorization: Bearer <admin-token>
```

**Note:** Protected default settings cannot be deleted. Attempting to delete them returns:
```json
{
  "message": "This setting is protected and cannot be deleted. You can only update its value."
}
```

---

### 8. Reset All Settings to Defaults
```http
POST /api/settings/reset-defaults
Authorization: Bearer <admin-token>
```

**Response:**
```json
{
  "message": "All settings reset to defaults",
  "restoredCount": 31
}
```

---

### 9. Get Available Categories
```http
GET /api/settings/categories
Authorization: Bearer <admin-token>
```

**Response:**
```json
{
  "categories": [
    "BackgroundJobs",
    "JWT",
    "Gemini",
    "Scraping",
    "Pagination",
    "Analytics",
    "Logging"
  ]
}
```

---

## 🔄 How Background Jobs Read Settings

### On Application Startup (Program.cs):
```csharp
using (var scope = app.Services.CreateScope())
{
    var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
    
    // Read from database
    var enabled = await settingsService.GetSettingAsBoolAsync(
        "BackgroundJobs:LiveTendersSync:Enabled", true);
    
    var intervalHours = await settingsService.GetSettingAsDoubleAsync(
        "BackgroundJobs:LiveTendersSync:IntervalHours", 0.0833);
    
    if (enabled)
    {
        var cronExpression = $"*/{(int)(intervalHours * 60)} * * * *";
        RecurringJob.AddOrUpdate<IDatabaseSyncService>(
            "live-tenders-sync",
            service => service.SyncLiveTendersAsync(),
            cronExpression
        );
    }
}
```

### Fallback Mechanism:
1. **Try Database**: Read from `SystemSettings` table
2. **Try Config**: If database fails, read from `appsettings.json`
3. **Use Default**: If config missing, use default parameter value
4. **Log Warning**: Every fallback is logged for debugging

---

## 📝 Common Use Cases

### 1. Change Live Tenders Sync to Every 10 Minutes
```bash
curl -X PUT "https://your-api/api/settings/key/BackgroundJobs:LiveTendersSync:IntervalHours" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"value": "0.1667"}'
```

Then **restart the application** for changes to take effect.

---

### 2. Increase JWT Token Lifetime to 48 Hours
```bash
curl -X PUT "https://your-api/api/settings/key/JWT:ExpirationMinutes" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"value": "2880"}'
```

Restart required for JWT changes.

---

### 3. Disable AI Features Temporarily
```bash
curl -X PUT "https://your-api/api/settings/key/Gemini:Enabled" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"value": "false"}'
```

Affects runtime behavior immediately (no restart needed for Gemini service).

---

### 4. Bulk Update All Background Job Intervals
```bash
curl -X PUT "https://your-api/api/settings/bulk" \
  -H "Authorization: Bearer YOUR_ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "updates": [
      {"key": "BackgroundJobs:LiveTendersSync:IntervalHours", "value": "0.1"},
      {"key": "BackgroundJobs:ClosedTendersSync:IntervalHours", "value": "12"},
      {"key": "BackgroundJobs:AwardNoticesSync:IntervalHours", "value": "12"},
      {"key": "BackgroundJobs:ProcurementPlansSync:IntervalHours", "value": "84"},
      {"key": "BackgroundJobs:VerifyAndMoveTenders:IntervalHours", "value": "3"}
    ]
  }'
```

---

## 🛠️ Direct Database Access (Alternative)

If API is unavailable, you can modify settings directly in SQL Server:

```sql
-- View all settings
SELECT * FROM SystemSettings WHERE IsDeleted = 0;

-- Update live tenders interval to 10 minutes (0.1667 hours)
UPDATE SystemSettings
SET Value = '0.1667', 
    UpdatedAt = GETUTCDATE(), 
    UpdatedBy = 'Admin-SQL'
WHERE [Key] = 'BackgroundJobs:LiveTendersSync:IntervalHours';

-- Disable a background job
UPDATE SystemSettings
SET Value = 'false'
WHERE [Key] = 'BackgroundJobs:ProcurementPlansSync:Enabled';

-- View settings by category
SELECT * FROM SystemSettings 
WHERE Category = 'BackgroundJobs' 
  AND IsDeleted = 0
ORDER BY [Key];
```

---

## ⚠️ Important Notes

1. **Restart Required**: Changes to background job intervals require application restart
2. **Protected Settings**: Default 31 settings cannot be deleted, only updated
3. **Audit Trail**: All changes tracked with `UpdatedAt`, `UpdatedBy`, `CreatedAt`, `CreatedBy`
4. **Soft Delete**: Deleted settings are marked `IsDeleted = 1`, not physically removed
5. **Type Safety**: Use specific methods:
   - `GetSettingAsIntAsync()` for integers
   - `GetSettingAsDoubleAsync()` for decimals
   - `GetSettingAsBoolAsync()` for boolean values
   - `GetSettingAsync()` for strings

---

## 🔍 Troubleshooting

### Settings Not Updating?
1. Check you're using an **Admin** JWT token
2. Verify the setting key is correct (case-sensitive)
3. Restart the application after background job changes
4. Check database connection in `appsettings.json`

### Fallback to Config File?
If you see warnings like:
```
⚠️  Failed to read BackgroundJobs:LiveTendersSync:Enabled from database, using config: True
```

**Causes:**
- Database connection issue
- SystemSettings table missing
- Migration not applied

**Solution:**
```bash
dotnet ef database update
```

---

## 🎓 Best Practices

1. **Use Bulk Updates**: For multiple changes, use bulk endpoint for efficiency
2. **Document Custom Settings**: Add clear descriptions when creating custom settings
3. **Monitor Logs**: Check application logs after settings changes
4. **Test in Dev First**: Test setting changes in development before production
5. **Backup Before Reset**: Export settings before using reset-defaults endpoint
6. **Version Control**: Keep `appsettings.json` as fallback in source control

---

## 📊 Database Schema

```sql
CREATE TABLE [SystemSettings] (
    [Id] int NOT NULL IDENTITY,
    [Key] nvarchar(200) NOT NULL,           -- Unique setting key
    [Value] nvarchar(max) NOT NULL,         -- Setting value
    [Description] nvarchar(1000) NOT NULL,  -- Human-readable description
    [Category] nvarchar(100) NOT NULL,      -- Group by category
    [CreatedAt] datetime2 NOT NULL,         -- When created
    [CreatedBy] nvarchar(100) NOT NULL,     -- Who created
    [UpdatedAt] datetime2 NULL,             -- Last update time
    [UpdatedBy] nvarchar(100) NULL,         -- Who updated
    [IsDeleted] bit NOT NULL,               -- Soft delete flag
    [DeletedAt] datetime2 NULL,             -- When deleted
    [DeletedBy] nvarchar(100) NULL,         -- Who deleted
    [RowVersion] rowversion NULL,           -- Optimistic concurrency
    CONSTRAINT [PK_SystemSettings] PRIMARY KEY ([Id])
);

CREATE UNIQUE INDEX [IX_SystemSettings_Key] ON [SystemSettings] ([Key]);
```

---

## ✅ Migration Applied

**Migration:** `20260127051756_AddSystemSettingsTable`
- ✅ Created `SystemSettings` table
- ✅ Seeded 31 default settings
- ✅ Created unique index on `Key` column
- ✅ Added audit trail columns

---

## 🔗 Related Files

- **Entity:** `ZimbabweTenderAPI/Data/Entities/Entities.cs` (SystemSettingEntity)
- **Controller:** `ZimbabweTenderAPI/Controllers/SettingsController.cs`
- **Service:** `ZimbabweTenderAPI/Services/SettingsService.cs`
- **Context:** `ZimbabweTenderAPI/Data/ApplicationDbContext.cs`
- **Startup:** `ZimbabweTenderAPI/Program.cs`

---

**Last Updated:** 2026-01-27  
**Version:** 1.0  
**Author:** System
