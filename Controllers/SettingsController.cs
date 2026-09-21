using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(GroupName = "system")]
    [Authorize(Policy = "AdminOnly")]
    public class SettingsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public SettingsController(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Get all system settings grouped by category
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetAllSettings()
        {
            var settings = await _context.SystemSettings
                .OrderBy(s => s.Category)
                .ThenBy(s => s.Key)
                .ToListAsync();

            var grouped = settings.GroupBy(s => s.Category)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(s => new
                    {
                        s.Key,
                        s.Value,
                        s.Description,
                        s.UpdatedAt,
                        s.UpdatedBy
                    }).ToList()
                );

            return Ok(new
            {
                totalSettings = settings.Count,
                categories = grouped.Keys.ToList(),
                settings = grouped
            });
        }

        /// <summary>
        /// Get settings by category (BackgroundJobs, JWT, Gemini, Scraping, Database)
        /// </summary>
        [HttpGet("category/{category}")]
        public async Task<IActionResult> GetSettingsByCategory(string category)
        {
            var settings = await _context.SystemSettings
                .Where(s => s.Category == category)
                .OrderBy(s => s.Key)
                .Select(s => new
                {
                    s.Key,
                    s.Value,
                    s.Description,
                    s.UpdatedAt,
                    s.UpdatedBy
                })
                .ToListAsync();

            if (!settings.Any())
            {
                return NotFound(new { error = $"No settings found for category '{category}'" });
            }

            return Ok(new
            {
                category,
                count = settings.Count,
                settings
            });
        }

        /// <summary>
        /// Get a specific setting by key
        /// </summary>
        [HttpGet("key/{key}")]
        public async Task<IActionResult> GetSetting(string key)
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == key);

            if (setting == null)
            {
                return NotFound(new { error = $"Setting '{key}' not found" });
            }

            return Ok(new
            {
                setting.Key,
                setting.Value,
                setting.Description,
                setting.Category,
                setting.UpdatedAt,
                setting.UpdatedBy
            });
        }

        /// <summary>
        /// Update a setting value (Admin only)
        /// </summary>
        [HttpPut("key/{key}")]
        public async Task<IActionResult> UpdateSetting(string key, [FromBody] UpdateSettingRequest request)
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == key);

            if (setting == null)
            {
                return NotFound(new { error = $"Setting '{key}' not found" });
            }

            var oldValue = setting.Value;
            setting.Value = request.Value;
            setting.UpdatedAt = DateTime.UtcNow;
            setting.UpdatedBy = User.Identity?.Name ?? "Admin";

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Setting '{key}' updated successfully",
                key,
                oldValue,
                newValue = setting.Value,
                updatedBy = setting.UpdatedBy,
                updatedAt = setting.UpdatedAt
            });
        }

        /// <summary>
        /// Bulk update multiple settings at once
        /// </summary>
        [HttpPut("bulk")]
        public async Task<IActionResult> BulkUpdateSettings([FromBody] List<BulkUpdateSettingRequest> requests)
        {
            var updated = new List<object>();
            var failed = new List<object>();

            foreach (var request in requests)
            {
                var setting = await _context.SystemSettings
                    .FirstOrDefaultAsync(s => s.Key == request.Key);

                if (setting == null)
                {
                    failed.Add(new { key = request.Key, reason = "Setting not found" });
                    continue;
                }

                var oldValue = setting.Value;
                setting.Value = request.Value;
                setting.UpdatedAt = DateTime.UtcNow;
                setting.UpdatedBy = User.Identity?.Name ?? "Admin";

                updated.Add(new
                {
                    key = request.Key,
                    oldValue,
                    newValue = setting.Value
                });
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"{updated.Count} settings updated successfully",
                updated,
                failed,
                updatedBy = User.Identity?.Name ?? "Admin",
                updatedAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Create a new custom setting (Admin only)
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateSetting([FromBody] CreateSettingRequest request)
        {
            var existing = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == request.Key);

            if (existing != null)
            {
                return BadRequest(new { error = $"Setting with key '{request.Key}' already exists" });
            }

            var setting = new SystemSettingEntity
            {
                Key = request.Key,
                Value = request.Value,
                Description = request.Description ?? "",
                Category = request.Category ?? "Custom",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = User.Identity?.Name ?? "Admin"
            };

            await _context.SystemSettings.AddAsync(setting);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSetting), new { key = setting.Key }, new
            {
                message = "Setting created successfully",
                setting = new
                {
                    setting.Key,
                    setting.Value,
                    setting.Description,
                    setting.Category,
                    setting.CreatedAt,
                    setting.CreatedBy
                }
            });
        }

        /// <summary>
        /// Delete a custom setting (Admin only) - Cannot delete system-critical settings
        /// </summary>
        [HttpDelete("key/{key}")]
        public async Task<IActionResult> DeleteSetting(string key)
        {
            // Protect critical settings from deletion
            var protectedKeys = new[]
            {
                "BackgroundJobs:LiveTendersSync:Enabled",
                "BackgroundJobs:ClosedTendersSync:Enabled",
                "JWT:SecretKey",
                "Database:ConnectionString"
            };

            if (protectedKeys.Contains(key))
            {
                return BadRequest(new { error = $"Cannot delete protected setting '{key}'" });
            }

            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == key);

            if (setting == null)
            {
                return NotFound(new { error = $"Setting '{key}' not found" });
            }

            _context.SystemSettings.Remove(setting);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = $"Setting '{key}' deleted successfully",
                key,
                deletedBy = User.Identity?.Name ?? "Admin",
                deletedAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Reset all settings to default values
        /// </summary>
        [HttpPost("reset-defaults")]
        public async Task<IActionResult> ResetToDefaults()
        {
            var defaultSettings = GetDefaultSettings();
            var updated = 0;

            foreach (var defaultSetting in defaultSettings)
            {
                var setting = await _context.SystemSettings
                    .FirstOrDefaultAsync(s => s.Key == defaultSetting.Key);

                if (setting != null)
                {
                    setting.Value = defaultSetting.Value;
                    setting.UpdatedAt = DateTime.UtcNow;
                    setting.UpdatedBy = "System Reset";
                    updated++;
                }
                else
                {
                    await _context.SystemSettings.AddAsync(new SystemSettingEntity
                    {
                        Key = defaultSetting.Key,
                        Value = defaultSetting.Value,
                        Description = defaultSetting.Description,
                        Category = defaultSetting.Category,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = "System Reset"
                    });
                    updated++;
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "All settings reset to default values",
                settingsReset = updated,
                resetBy = User.Identity?.Name ?? "Admin",
                resetAt = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Get available setting categories
        /// </summary>
        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var categories = await _context.SystemSettings
                .Select(s => s.Category)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync();

            return Ok(new
            {
                count = categories.Count,
                categories
            });
        }

        // Helper method to define default settings
        private List<SystemSettingEntity> GetDefaultSettings()
        {
            return new List<SystemSettingEntity>
            {
                // Background Jobs - Live Tenders
                new() { Key = "BackgroundJobs:LiveTendersSync:Enabled", Value = "true", Description = "Enable/disable live tenders sync job", Category = "BackgroundJobs" },
                new() { Key = "BackgroundJobs:LiveTendersSync:IntervalHours", Value = "0.0833", Description = "Live tenders sync interval in hours (0.0833 = 5 minutes)", Category = "BackgroundJobs" },
                
                // Background Jobs - Closed Tenders
                new() { Key = "BackgroundJobs:ClosedTendersSync:Enabled", Value = "true", Description = "Enable/disable closed tenders sync job", Category = "BackgroundJobs" },
                new() { Key = "BackgroundJobs:ClosedTendersSync:IntervalHours", Value = "24", Description = "Closed tenders sync interval in hours", Category = "BackgroundJobs" },
                new() { Key = "BackgroundJobs:ClosedTendersSync:RunAtHour", Value = "3", Description = "Hour to run closed tenders sync (24-hour format)", Category = "BackgroundJobs" },
                
                // Background Jobs - Award Notices
                new() { Key = "BackgroundJobs:AwardNoticesSync:Enabled", Value = "true", Description = "Enable/disable award notices sync job", Category = "BackgroundJobs" },
                new() { Key = "BackgroundJobs:AwardNoticesSync:IntervalHours", Value = "24", Description = "Award notices sync interval in hours", Category = "BackgroundJobs" },
                new() { Key = "BackgroundJobs:AwardNoticesSync:RunAtHour", Value = "2", Description = "Hour to run award notices sync (24-hour format)", Category = "BackgroundJobs" },
                
                // Background Jobs - Procurement Plans
                new() { Key = "BackgroundJobs:ProcurementPlansSync:Enabled", Value = "true", Description = "Enable/disable procurement plans sync job", Category = "BackgroundJobs" },
                new() { Key = "BackgroundJobs:ProcurementPlansSync:IntervalHours", Value = "168", Description = "Procurement plans sync interval in hours (168 = weekly)", Category = "BackgroundJobs" },
                new() { Key = "BackgroundJobs:ProcurementPlansSync:RunAtHour", Value = "4", Description = "Hour to run procurement plans sync (24-hour format)", Category = "BackgroundJobs" },
                
                // Background Jobs - Verify and Move
                new() { Key = "BackgroundJobs:VerifyAndMoveTenders:Enabled", Value = "true", Description = "Enable/disable verify and move tenders job", Category = "BackgroundJobs" },
                new() { Key = "BackgroundJobs:VerifyAndMoveTenders:IntervalHours", Value = "6", Description = "Verify and move interval in hours", Category = "BackgroundJobs" },
                
                // JWT Settings
                new() { Key = "JWT:ExpirationMinutes", Value = "1440", Description = "JWT token expiration time in minutes (1440 = 24 hours)", Category = "JWT" },
                new() { Key = "JWT:RefreshTokenExpirationDays", Value = "7", Description = "Refresh token expiration in days", Category = "JWT" },
                
                // Gemini AI Settings
                new() { Key = "Gemini:Model", Value = "gemini-2.0-flash-exp", Description = "Gemini AI model to use", Category = "Gemini" },
                new() { Key = "Gemini:Temperature", Value = "0.7", Description = "Gemini AI temperature (0.0-1.0)", Category = "Gemini" },
                new() { Key = "Gemini:MaxTokens", Value = "8000", Description = "Maximum tokens for Gemini responses", Category = "Gemini" },
                new() { Key = "Gemini:Enabled", Value = "true", Description = "Enable/disable Gemini AI features", Category = "Gemini" },
                
                // Scraping Settings
                new() { Key = "Scraping:BatchSize", Value = "100", Description = "Number of items to process in each batch", Category = "Scraping" },
                new() { Key = "Scraping:TimeoutSeconds", Value = "30", Description = "HTTP request timeout in seconds", Category = "Scraping" },
                new() { Key = "Scraping:RetryAttempts", Value = "3", Description = "Number of retry attempts for failed requests", Category = "Scraping" },
                new() { Key = "Scraping:DelayBetweenRequestsMs", Value = "500", Description = "Delay between scraping requests in milliseconds", Category = "Scraping" },
                
                // Pagination Settings
                new() { Key = "Pagination:DefaultPageSize", Value = "20", Description = "Default number of items per page", Category = "Pagination" },
                new() { Key = "Pagination:MaxPageSize", Value = "100", Description = "Maximum allowed page size", Category = "Pagination" },
                
                // Analytics Settings
                new() { Key = "Analytics:DefaultDaysBack", Value = "90", Description = "Default days to look back for analytics", Category = "Analytics" },
                new() { Key = "Analytics:MinConfidenceScore", Value = "60", Description = "Minimum confidence score for recommendations", Category = "Analytics" },
                new() { Key = "Analytics:MaxOpportunities", Value = "50", Description = "Maximum number of opportunities to return", Category = "Analytics" },
                
                // Logging Settings
                new() { Key = "Logging:EnableFileLogging", Value = "true", Description = "Enable logging to files", Category = "Logging" },
                new() { Key = "Logging:LogPath", Value = "C:/Temp", Description = "Directory path for log files", Category = "Logging" },
                new() { Key = "Logging:RetentionDays", Value = "30", Description = "Number of days to keep log files", Category = "Logging" }
            };
        }
    }

    // DTOs
    public class UpdateSettingRequest
    {
        public string Value { get; set; } = string.Empty;
    }

    public class BulkUpdateSettingRequest
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class CreateSettingRequest
    {
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Category { get; set; }
    }
}
