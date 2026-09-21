using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.DTOs;

namespace ZimbabweTenderAPI.Controllers
{
    /// <summary>
    /// Provides endpoints to get ALL records without pagination.
    /// Use these for exporting complete datasets.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(GroupName = "database")]
    public class BulkDataController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<BulkDataController> _logger;

        public BulkDataController(ApplicationDbContext context, ILogger<BulkDataController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get ALL Live Tenders (no pagination)
        /// </summary>
        /// <param name="minDate">Optional: Filter by minimum publication date (format: yyyy-MM-dd)</param>
        /// <param name="maxDate">Optional: Filter by maximum publication date (format: yyyy-MM-dd)</param>
        /// <param name="entity">Optional: Filter by procuring entity (partial match)</param>
        [HttpGet("live-tenders")]
        [AllowAnonymous]
        [Produces("application/json")]
        public async Task<ActionResult<ApiResponse<List<TenderDto>>>> GetAllLiveTenders(
            [FromQuery] DateTime? minDate = null,
            [FromQuery] DateTime? maxDate = null,
            [FromQuery] string? entity = null)
        {
            try
            {
                var query = _context.LiveTenders.AsQueryable();

                // Apply filters
                if (minDate.HasValue)
                    query = query.Where(t => t.PublishDate >= minDate.Value);

                if (maxDate.HasValue)
                    query = query.Where(t => t.PublishDate <= maxDate.Value);

                if (!string.IsNullOrWhiteSpace(entity))
                    query = query.Where(t => t.ProcuringEntity.Contains(entity));

                var tenders = await query
                    .OrderByDescending(t => t.PublishDate)
                    .ToListAsync();

                var result = tenders.Select(t => new TenderDto
                {
                    TenderId = t.TenderId,
                    Title = t.Title,
                    ReferenceNumber = t.ReferenceNumber ?? "",
                    ProcuringEntity = t.ProcuringEntity,
                    CategoryCodes = string.IsNullOrEmpty(t.CategoryCodes) ? new List<string>() : System.Text.Json.JsonSerializer.Deserialize<List<string>>(t.CategoryCodes) ?? new List<string>(),
                    CategoryNames = string.IsNullOrEmpty(t.CategoryNames) ? new List<string>() : System.Text.Json.JsonSerializer.Deserialize<List<string>>(t.CategoryNames) ?? new List<string>(),
                    Scope = t.Scope ?? "",
                    PublishDate = t.PublishDate?.ToString("yyyy-MM-dd") ?? "",
                    ClosingDate = t.ClosingDate?.ToString("yyyy-MM-dd") ?? "",
                    DetailsUrl = t.DetailsUrl ?? ""
                })
                    .ToList();

                return Ok(new ApiResponse<List<TenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {result.Count} live tenders",
                    Data = result,
                    TotalCount = result.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all live tenders");
                return StatusCode(500, new ApiResponse<List<TenderDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get ALL Closed Tenders (no pagination)
        /// </summary>
        /// <param name="minDate">Optional: Filter by minimum closed date</param>
        /// <param name="maxDate">Optional: Filter by maximum closed date</param>
        /// <param name="entity">Optional: Filter by procuring entity</param>
        [HttpGet("closed-tenders")]
        [AllowAnonymous]
        [Produces("application/json")]
        public async Task<ActionResult<ApiResponse<List<TenderDto>>>> GetAllClosedTenders(
            [FromQuery] DateTime? minDate = null,
            [FromQuery] DateTime? maxDate = null,
            [FromQuery] string? entity = null)
        {
            try
            {
                var query = _context.ClosedTenders.AsQueryable();

                if (minDate.HasValue)
                    query = query.Where(t => t.ClosingDate >= minDate.Value);

                if (maxDate.HasValue)
                    query = query.Where(t => t.ClosingDate <= maxDate.Value);

                if (!string.IsNullOrWhiteSpace(entity))
                    query = query.Where(t => t.ProcuringEntity.Contains(entity));

                var tenders = await query
                    .OrderByDescending(t => t.ClosingDate)
                    .ToListAsync();

                var result = tenders.Select(t => new TenderDto
                {
                    TenderId = t.TenderId,
                    Title = t.Title,
                    ReferenceNumber = t.ReferenceNumber ?? "",
                    ProcuringEntity = t.ProcuringEntity,
                    CategoryCodes = string.IsNullOrEmpty(t.CategoryCodes) ? new List<string>() : System.Text.Json.JsonSerializer.Deserialize<List<string>>(t.CategoryCodes) ?? new List<string>(),
                    CategoryNames = string.IsNullOrEmpty(t.CategoryNames) ? new List<string>() : System.Text.Json.JsonSerializer.Deserialize<List<string>>(t.CategoryNames) ?? new List<string>(),
                    Scope = t.Scope ?? "",
                    PublishDate = t.PublishDate?.ToString("yyyy-MM-dd") ?? "",
                    ClosingDate = t.ClosingDate?.ToString("yyyy-MM-dd") ?? "",
                    DetailsUrl = t.DetailsUrl ?? ""
                })
                    .ToList();

                return Ok(new ApiResponse<List<TenderDto>>
                {
                    Success = true,
                    Message = $"Retrieved {result.Count} closed tenders",
                    Data = result,
                    TotalCount = result.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all closed tenders");
                return StatusCode(500, new ApiResponse<List<TenderDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get ALL Award Notices (no pagination)
        /// </summary>
        /// <param name="minDate">Optional: Filter by minimum award date</param>
        /// <param name="maxDate">Optional: Filter by maximum award date</param>
        /// <param name="awardee">Optional: Filter by awardee name</param>
        [HttpGet("award-notices")]
        [AllowAnonymous]
        [Produces("application/json")]
        public async Task<ActionResult<ApiResponse<List<AwardNoticeDto>>>> GetAllAwardNotices(
            [FromQuery] DateTime? minDate = null,
            [FromQuery] DateTime? maxDate = null,
            [FromQuery] string? awardee = null)
        {
            try
            {
                var query = _context.AwardNotices.AsQueryable();

                if (minDate.HasValue)
                    query = query.Where(a => a.ParsedAwardDate >= minDate.Value);

                if (maxDate.HasValue)
                    query = query.Where(a => a.ParsedAwardDate <= maxDate.Value);

                if (!string.IsNullOrWhiteSpace(awardee))
                    query = query.Where(a => a.Awardee.Contains(awardee));

                var awards = await query
                    .OrderByDescending(a => a.ParsedAwardDate)
                    .ToListAsync();

                var result = awards.Select(a => new AwardNoticeDto
                {
                    AwardNoticeNumber = a.AwardNoticeNumber,
                    TenderId = a.TenderId,
                    AwardTitle = a.AwardTitle,
                    Awardee = a.Awardee,
                    AwardDate = a.AwardDate,
                    ContractValue = a.ContractValue,
                    Currency = a.Currency,
                    DetailsUrl = a.DetailsUrl
                })
                    .ToList();

                return Ok(new ApiResponse<List<AwardNoticeDto>>
                {
                    Success = true,
                    Message = $"Retrieved {result.Count} award notices",
                    Data = result,
                    TotalCount = result.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all award notices");
                return StatusCode(500, new ApiResponse<List<AwardNoticeDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get count statistics for all data types
        /// </summary>
        [HttpGet("statistics")]
        [AllowAnonymous]
        [Produces("application/json")]
        public async Task<ActionResult<ApiResponse<BulkDataStatistics>>> GetStatistics()
        {
            try
            {
                var stats = new BulkDataStatistics
                {
                    LiveTendersCount = await _context.LiveTenders.CountAsync(),
                    ClosedTendersCount = await _context.ClosedTenders.CountAsync(),
                    AwardNoticesCount = await _context.AwardNotices.CountAsync(),
                    ProcurementPlansCount = await _context.ProcurementPlans.CountAsync(),
                    TotalRecords = 0 // Will be calculated below
                };

                stats.TotalRecords = stats.LiveTendersCount + stats.ClosedTendersCount +
                                   stats.AwardNoticesCount + stats.ProcurementPlansCount;

                // Get date ranges
                var earliestLiveTender = await _context.LiveTenders
                    .OrderBy(t => t.PublishDate)
                    .Select(t => t.PublishDate)
                    .FirstOrDefaultAsync();

                var latestLiveTender = await _context.LiveTenders
                    .OrderByDescending(t => t.PublishDate)
                    .Select(t => t.PublishDate)
                    .FirstOrDefaultAsync();

                stats.EarliestTenderDate = earliestLiveTender;
                stats.LatestTenderDate = latestLiveTender;
                stats.LastUpdated = DateTime.UtcNow;

                return Ok(new ApiResponse<BulkDataStatistics>
                {
                    Success = true,
                    Data = stats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting statistics");
                return StatusCode(500, new ApiResponse<BulkDataStatistics>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }
    }

    /// <summary>
    /// Statistics for bulk data export
    /// </summary>
    public class BulkDataStatistics
    {
        public int LiveTendersCount { get; set; }
        public int ClosedTendersCount { get; set; }
        public int AwardNoticesCount { get; set; }
        public int ProcurementPlansCount { get; set; }
        public int TotalRecords { get; set; }
        public DateTime? EarliestTenderDate { get; set; }
        public DateTime? LatestTenderDate { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}
