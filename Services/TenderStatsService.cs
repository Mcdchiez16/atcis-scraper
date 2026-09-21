using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace ZimbabweTenderAPI.Services
{
    public interface ITenderStatsService
    {
        Task<object> GetComprehensiveStatsAsync();
    }

    public class TenderStatsService : ITenderStatsService
    {
        private readonly ITenderScraperService _scraperService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TenderStatsService> _logger;

        public TenderStatsService(
            ITenderScraperService scraperService,
            IMemoryCache cache,
            ILogger<TenderStatsService> logger)
        {
            _scraperService = scraperService;
            _cache = cache;
            _logger = logger;
        }

        public async Task<object> GetComprehensiveStatsAsync()
        {
            try
            {
                var cacheKey = "comprehensive_stats";
                if (_cache.TryGetValue(cacheKey, out object cached))
                {
                    return cached;
                }

                // Get current tenders stats
                var firstPage = await _scraperService.ScrapePageAsync(1);
                var totalPages = await _scraperService.GetTotalPagesAsync();

                // Get past tenders stats (first page only for count)
                var pastTendersPage = await _scraperService.ScrapePastTendersAsync(1);

                // Get award notices stats (first page only)
                var awardNoticesPage = await _scraperService.ScrapeAwardNoticesAsync(1);

                // Get annual plans stats (first page only)
                var annualPlansPage = await _scraperService.ScrapeAnnualProcurementPlansAsync(1);

                var stats = new
                {
                    CurrentTenders = new
                    {
                        TotalPages = totalPages,
                        TendersPerPage = firstPage.Tenders.Count,
                        EstimatedTotal = totalPages * firstPage.Tenders.Count,
                        ClosingSoon = firstPage.Tenders.Count(t =>
                            t.ClosingDate.HasValue &&
                            t.ClosingDate > DateTime.UtcNow &&
                            t.ClosingDate <= DateTime.UtcNow.AddDays(7))
                    },
                    PastTenders = new
                    {
                        TotalPages = (int)Math.Ceiling(pastTendersPage.TotalCount / 20.0),
                        TotalCount = pastTendersPage.TotalCount
                    },
                    AwardNotices = new
                    {
                        TotalPages = (int)Math.Ceiling(awardNoticesPage.TotalCount / 20.0),
                        TotalCount = awardNoticesPage.TotalCount
                    },
                    AnnualProcurementPlans = new
                    {
                        TotalPages = (int)Math.Ceiling(annualPlansPage.TotalCount / 20.0),
                        TotalCount = annualPlansPage.TotalCount
                    },
                    SystemInfo = new
                    {
                        LastScraped = DateTime.UtcNow,
                        BaseUrl = "https://egp.praz.org.zw",
                        CacheSize = "Not Implemented",
                        Uptime = "Not Implemented"
                    },
                    Timestamp = DateTime.UtcNow
                };

                _cache.Set(cacheKey, stats, TimeSpan.FromMinutes(10));
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting comprehensive stats");
                throw;
            }
        }
    }
}