using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    // ==================== SYNC CONTROLLER ====================
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AdminOnly")]
    [ApiExplorerSettings(GroupName = "system")]
    public class SyncController : ControllerBase
    {
        private readonly IDatabaseSyncService _syncService;
        private readonly ITenderScraperService _scraperService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<SyncController> _logger;

        public SyncController(
            IDatabaseSyncService syncService,
            ITenderScraperService scraperService,
            ApplicationDbContext context,
            ILogger<SyncController> logger)
        {
            _syncService = syncService;
            _scraperService = scraperService;
            _context = context;
            _logger = logger;
        }

        [HttpPost("live-tenders")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<SyncResult>>> SyncLiveTenders([FromQuery] int pages = 5)
        {
            try
            {
                var tenders = await _scraperService.ScrapeMultiplePagesAsync(1, pages);
                var result = await _syncService.SyncLiveTendersAsync(tenders);
                return Ok(new ApiResponse<SyncResult> { Success = result.Success, Message = result.Summary, Data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing live tenders");
                return StatusCode(500, new ApiResponse<SyncResult> { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("closed-tenders")]
        public async Task<ActionResult<ApiResponse<SyncResult>>> SyncClosedTenders([FromQuery] int pages = 3)
        {
            try
            {
                var tenders = await _scraperService.ScrapeMultiplePastTenderPagesAsync(1, pages);
                var result = await _syncService.SyncClosedTendersAsync(tenders);
                return Ok(new ApiResponse<SyncResult> { Success = result.Success, Message = result.Summary, Data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing closed tenders");
                return StatusCode(500, new ApiResponse<SyncResult> { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("awards")]
        public async Task<ActionResult<ApiResponse<SyncResult>>> SyncAwards([FromQuery] int pages = 3)
        {
            try
            {
                var awards = await _scraperService.ScrapeMultipleAwardNoticePagesAsync(1, pages);
                var result = await _syncService.SyncAwardNoticesAsync(awards);
                return Ok(new ApiResponse<SyncResult> { Success = result.Success, Message = result.Summary, Data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing awards");
                return StatusCode(500, new ApiResponse<SyncResult> { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("plans")]
        public async Task<ActionResult<ApiResponse<SyncResult>>> SyncPlans([FromQuery] int pages = 3)
        {
            try
            {
                var plans = await _scraperService.ScrapeMultipleAnnualProcurementPlanPagesAsync(1, pages);
                var result = await _syncService.SyncProcurementPlansAsync(plans);
                return Ok(new ApiResponse<SyncResult> { Success = result.Success, Message = result.Summary, Data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error syncing plans");
                return StatusCode(500, new ApiResponse<SyncResult> { Success = false, Message = ex.Message });
            }
        }

        [HttpPost("verify-and-move")]
        public async Task<ActionResult<ApiResponse<SyncResult>>> VerifyAndMove()
        {
            try
            {
                var result = await _syncService.VerifyAndMoveTendersAsync();
                return Ok(new ApiResponse<SyncResult> { Success = result.Success, Message = result.Summary, Data = result });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying and moving tenders");
                return StatusCode(500, new ApiResponse<SyncResult> { Success = false, Message = ex.Message });
            }
        }

        [HttpGet("history")]
        public async Task<ActionResult<ApiResponse<List<ScrapingJobHistoryEntity>>>> GetHistory([FromQuery] int limit = 50)
        {
            var history = await _context.ScrapingJobHistory
                .OrderByDescending(h => h.StartTime)
                .Take(limit)
                .ToListAsync();
            return Ok(new ApiResponse<List<ScrapingJobHistoryEntity>> { Success = true, Data = history });
        }

        [HttpGet("status")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<object>>> GetStatus()
        {
            var liveTendersCount = await _context.LiveTenders.CountAsync();
            var closedTendersCount = await _context.ClosedTenders.CountAsync();
            var awardsCount = await _context.AwardNotices.CountAsync();
            var plansCount = await _context.ProcurementPlans.CountAsync();
            var lastSync = await _context.ScrapingJobHistory
                .OrderByDescending(h => h.EndTime)
                .FirstOrDefaultAsync();

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = new
                {
                    LiveTenders = liveTendersCount,
                    ClosedTenders = closedTendersCount,
                    Awards = awardsCount,
                    Plans = plansCount,
                    LastSync = lastSync?.EndTime,
                    LastSyncStatus = lastSync?.Status
                }
            });
        }
    }
}
