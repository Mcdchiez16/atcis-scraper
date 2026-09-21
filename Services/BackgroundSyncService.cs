using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Models;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Services
{
    public class BackgroundSyncService
    {
        private readonly ITenderScraperService _scraperService;
        private readonly IDatabaseSyncService _syncService;
        private readonly ILogger<BackgroundSyncService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IMultiSourceProcurementService _multiSourceService;
        private readonly int _maxPagesLimit;
        private readonly string _debugFilePath = "C:/Temp/tender_debug.txt";

        public BackgroundSyncService(
            ITenderScraperService scraperService,
            IDatabaseSyncService syncService,
            IMultiSourceProcurementService multiSourceService,
            ILogger<BackgroundSyncService> logger,
            IConfiguration configuration)
        {
            _scraperService = scraperService;
            _syncService = syncService;
            _multiSourceService = multiSourceService;
            _logger = logger;
            _configuration = configuration;
            _maxPagesLimit = _configuration.GetValue<int>("TenderScraper:MaxPagesLimit", 50);

            // Ensure directory exists for debugging
            try { Directory.CreateDirectory("C:/Temp"); } catch { }
        }

        private void LogToDebugFile(string message)
        {
            try
            {
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(_debugFilePath, logEntry);
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Failed to write to debug file: {ex.Message}");
            }
        }

        public async Task SyncLiveTendersAsync()
        {
            try
            {
                LogToDebugFile("START: SyncLiveTendersAsync");
                _logger.LogInformation($"[10-MIN BACKGROUND SCRAPER] Starting live tenders sync job (Scraping up to {_maxPagesLimit} pages from PRAZ e-GP)");

                var tenders = await _scraperService.ScrapeMultiplePagesAsync(1, _maxPagesLimit);
                LogToDebugFile($"PROGRESS: Scraped {tenders.Count} live tenders from PRAZ");

                var result = await _syncService.SyncLiveTendersAsync(tenders);
                _logger.LogInformation($"[PRAZ SYNC] Live tenders sync completed: {result.Summary}");
                LogToDebugFile($"SUCCESS: Live sync completed. {result.Summary}");

                // Also trigger 10-minute automated sync for Multi-Source portals (OnlineTenders, ZPPA, World Bank, UNGM, AfDB)
                try
                {
                    _logger.LogInformation("[10-MIN BACKGROUND SCRAPER] Refreshing multi-source portals (OnlineTenders, ZPPA, World Bank, UNGM, AfDB)...");
                    var multiSourceOpportunities = await _multiSourceService.GetUnifiedProcurementFeedAsync(limit: 100);
                    _logger.LogInformation($"[MULTI-SOURCE SYNC] Refreshed {multiSourceOpportunities.Count} external procurement opportunities.");
                }
                catch (Exception msEx)
                {
                    _logger.LogWarning(msEx, "Multi-source sync encountered a non-fatal warning during background cycle.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in live tenders sync job");
                LogToDebugFile($"ERROR: Live sync failed: {ex.Message}");
                throw;
            }
        }

        public async Task SyncClosedTendersAsync()
        {
            try
            {
                LogToDebugFile("START: SyncClosedTendersAsync");
                _logger.LogInformation($"Starting closed tenders sync job (Scraping up to {_maxPagesLimit} pages)");

                var tenders = await _scraperService.ScrapeMultiplePastTenderPagesAsync(1, _maxPagesLimit);
                LogToDebugFile($"PROGRESS: Scraped {tenders.Count} closed tenders");

                var result = await _syncService.SyncClosedTendersAsync(tenders);

                _logger.LogInformation($"Closed tenders sync completed: {result.Summary}");
                LogToDebugFile($"SUCCESS: Closed sync completed. {result.Summary}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in closed tenders sync job");
                LogToDebugFile($"ERROR: Closed sync failed: {ex.Message}");
                throw;
            }
        }

        public async Task SyncAwardNoticesAsync()
        {
            try
            {
                LogToDebugFile("START: SyncAwardNoticesAsync");
                _logger.LogInformation($"Starting award notices sync job (Scraping up to {_maxPagesLimit} pages)");

                var awards = await _scraperService.ScrapeMultipleAwardNoticePagesAsync(1, _maxPagesLimit);
                LogToDebugFile($"PROGRESS: Scraped {awards.Count} award notices");

                var result = await _syncService.SyncAwardNoticesAsync(awards);

                _logger.LogInformation($"Award notices sync completed: {result.Summary}");
                LogToDebugFile($"SUCCESS: Award sync completed. {result.Summary}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in award notices sync job");
                LogToDebugFile($"ERROR: Award sync failed: {ex.Message}");
                throw;
            }
        }

        public async Task SyncProcurementPlansAsync()
        {
            try
            {
                LogToDebugFile("START: SyncProcurementPlansAsync");
                _logger.LogInformation($"Starting procurement plans sync job (Scraping up to {_maxPagesLimit} pages)");

                var plans = await _scraperService.ScrapeMultipleAnnualProcurementPlanPagesAsync(1, _maxPagesLimit);
                LogToDebugFile($"PROGRESS: Scraped {plans.Count} procurement plans");

                var result = await _syncService.SyncProcurementPlansAsync(plans);

                _logger.LogInformation($"Procurement plans sync completed: {result.Summary}");
                LogToDebugFile($"SUCCESS: Procurement plans completed. {result.Summary}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in procurement plans sync job");
                LogToDebugFile($"ERROR: Procurement sync failed: {ex.Message}");
                throw;
            }
        }

        public async Task SyncProcurementPlanItemsAsync()
        {
            try
            {
                LogToDebugFile("START: SyncProcurementPlanItemsAsync");
                _logger.LogInformation("Starting procurement plan items sync job");

                // Get all plans with ViewAppUrl (plan detail pages)
                var plans = await _syncService.GetAllProcurementPlansWithUrlsAsync();
                LogToDebugFile($"PROGRESS: Found {plans.Count} plans with detail URLs");

                int totalScraped = 0;
                int successCount = 0;
                int failCount = 0;

                foreach (var plan in plans)
                {
                    try
                    {
                        var planDetail = await _scraperService.GetAnnualProcurementPlanDetailAsync(plan.ViewAppUrl);
                        if (planDetail?.Items?.Count > 0)
                        {
                            var result = await _syncService.SyncProcurementPlanItemsAsync(plan.Id, planDetail.Items);
                            totalScraped += planDetail.Items.Count;
                            successCount++;
                            LogToDebugFile($"SUCCESS: Plan {plan.Id} - {result.Summary}");
                        }
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        _logger.LogWarning($"Failed to scrape items for plan {plan.Id}: {ex.Message}");
                        LogToDebugFile($"ERROR: Plan {plan.Id} failed: {ex.Message}");
                    }
                }

                _logger.LogInformation($"Procurement plan items sync completed: {successCount} plans processed, {totalScraped} items total, {failCount} failures");
                LogToDebugFile($"COMPLETE: {successCount} plans, {totalScraped} items, {failCount} failures");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in procurement plan items sync job");
                LogToDebugFile($"ERROR: Items sync failed: {ex.Message}");
                throw;
            }
        }

        public async Task VerifyAndMoveTendersAsync()
        {
            try
            {
                LogToDebugFile("START: VerifyAndMoveTendersAsync");
                _logger.LogInformation("Starting verify and move tenders job");

                var result = await _syncService.VerifyAndMoveTendersAsync();

                _logger.LogInformation($"Verify and move tenders completed: {result.Summary}");
                LogToDebugFile($"SUCCESS: Verify/Move completed. {result.Summary}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in verify and move tenders job");
                LogToDebugFile($"ERROR: Verify/Move failed: {ex.Message}");
                throw;
            }
        }
    }
}