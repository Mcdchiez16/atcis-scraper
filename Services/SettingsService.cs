using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Data;

namespace ZimbabweTenderAPI.Services
{
    public interface ISettingsService
    {
        Task<string> GetSettingAsync(string key, string defaultValue = "");
        Task<int> GetSettingAsIntAsync(string key, int defaultValue = 0);
        Task<double> GetSettingAsDoubleAsync(string key, double defaultValue = 0.0);
        Task<bool> GetSettingAsBoolAsync(string key, bool defaultValue = false);
        Task SetSettingAsync(string key, string value, string updatedBy = "System");
        Task<T> GetSettingAsync<T>(string key, T defaultValue) where T : struct;
    }

    public class SettingsService : ISettingsService
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;

        public SettingsService(ApplicationDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        /// <summary>
        /// Get setting value from database, fallback to appsettings.json
        /// </summary>
        public async Task<string> GetSettingAsync(string key, string defaultValue = "")
        {
            try
            {
                // Try database first
                var setting = await _context.SystemSettings
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Key == key);

                if (setting != null)
                {
                    return setting.Value;
                }

                // Fallback to appsettings.json
                var configValue = _configuration[key];
                if (!string.IsNullOrEmpty(configValue))
                {
                    return configValue;
                }

                // Return default
                return defaultValue;
            }
            catch
            {
                // If database is unavailable, use appsettings
                return _configuration[key] ?? defaultValue;
            }
        }

        /// <summary>
        /// Get setting as integer
        /// </summary>
        public async Task<int> GetSettingAsIntAsync(string key, int defaultValue = 0)
        {
            var value = await GetSettingAsync(key, defaultValue.ToString());
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Get setting as double
        /// </summary>
        public async Task<double> GetSettingAsDoubleAsync(string key, double defaultValue = 0.0)
        {
            var value = await GetSettingAsync(key, defaultValue.ToString());
            return double.TryParse(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Get setting as boolean
        /// </summary>
        public async Task<bool> GetSettingAsBoolAsync(string key, bool defaultValue = false)
        {
            var value = await GetSettingAsync(key, defaultValue.ToString());
            return bool.TryParse(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Get setting with generic type conversion
        /// </summary>
        public async Task<T> GetSettingAsync<T>(string key, T defaultValue) where T : struct
        {
            var value = await GetSettingAsync(key, defaultValue.ToString() ?? "");
            
            try
            {
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }

        /// <summary>
        /// Set/update a setting value in database
        /// </summary>
        public async Task SetSettingAsync(string key, string value, string updatedBy = "System")
        {
            var setting = await _context.SystemSettings
                .FirstOrDefaultAsync(s => s.Key == key);

            if (setting != null)
            {
                setting.Value = value;
                setting.UpdatedAt = DateTime.UtcNow;
                setting.UpdatedBy = updatedBy;
            }
            else
            {
                setting = new Data.Entities.SystemSettingEntity
                {
                    Key = key,
                    Value = value,
                    Description = $"Auto-created setting",
                    Category = "Custom",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = updatedBy
                };
                await _context.SystemSettings.AddAsync(setting);
            }

            await _context.SaveChangesAsync();
        }
    }
}
