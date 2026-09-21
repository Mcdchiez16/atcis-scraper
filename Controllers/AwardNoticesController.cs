using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AwardNoticesController : ControllerBase
    {
        private readonly ITenderScraperService _scraperService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<AwardNoticesController> _logger;

        public AwardNoticesController(
            ITenderScraperService scraperService,
            IMemoryCache cache,
            ILogger<AwardNoticesController> logger)
        {
            _scraperService = scraperService;
            _cache = cache;
            _logger = logger;
        }

        // GET: api/award-notices/page/{pageNumber}
        [HttpGet("page/{pageNumber}")]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<AwardNoticeDto>>>> GetPage(int pageNumber)
        {
            try
            {
                var cacheKey = $"award_notices_page_{pageNumber}";
                if (!_cache.TryGetValue(cacheKey, out PaginatedResponse<AwardNoticeDto> awardNotices))
                {
                    awardNotices = await _scraperService.ScrapeAwardNoticesAsync(pageNumber);
                    _cache.Set(cacheKey, awardNotices, TimeSpan.FromMinutes(10));
                }

                return Ok(new ApiResponse<PaginatedResponse<AwardNoticeDto>>
                {
                    Success = true,
                    Message = $"Successfully scraped award notices page {pageNumber}",
                    Data = awardNotices,
                    TotalCount = awardNotices.TotalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scraping award notices page {pageNumber}");
                return StatusCode(500, new ApiResponse<PaginatedResponse<AwardNoticeDto>>
                {
                    Success = false,
                    Message = $"Error scraping award notices page: {ex.Message}"
                });
            }
        }

        // GET: api/award-notices/pages?start=1&end=5
        [HttpGet("pages")]
        public async Task<ActionResult<ApiResponse<List<AwardNoticeDto>>>> GetMultiplePages(
            [FromQuery] int start = 1,
            [FromQuery] int end = 5)
        {
            try
            {
                if (start < 1 || end < start)
                {
                    return BadRequest(new ApiResponse<List<AwardNoticeDto>>
                    {
                        Success = false,
                        Message = "Invalid page range. Start must be >= 1 and end >= start"
                    });
                }

                var awardNotices = await _scraperService.ScrapeMultipleAwardNoticePagesAsync(start, end);

                return Ok(new ApiResponse<List<AwardNoticeDto>>
                {
                    Success = true,
                    Message = $"Successfully scraped award notice pages {start}-{end}",
                    Data = awardNotices,
                    TotalCount = awardNotices.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scraping award notice pages {start}-{end}");
                return StatusCode(500, new ApiResponse<List<AwardNoticeDto>>
                {
                    Success = false,
                    Message = $"Error scraping award notice pages: {ex.Message}"
                });
            }
        }

        // POST: api/award-notices/search
        [HttpPost("search")]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<AwardNoticeDto>>>> Search(
            [FromBody] AwardNoticeSearchRequest request)
        {
            try
            {
                // Set default values if not provided
                request.Page = request.Page <= 0 ? 1 : request.Page;
                request.PageSize = request.PageSize <= 0 ? 20 : request.PageSize;

                var awardNotices = await _scraperService.SearchAwardNoticesAsync(request);

                return Ok(new ApiResponse<PaginatedResponse<AwardNoticeDto>>
                {
                    Success = true,
                    Message = "Successfully searched award notices",
                    Data = awardNotices,
                    TotalCount = awardNotices.TotalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching award notices");
                return StatusCode(500, new ApiResponse<PaginatedResponse<AwardNoticeDto>>
                {
                    Success = false,
                    Message = $"Error searching award notices: {ex.Message}"
                });
            }
        }

        // GET: api/award-notices/simple-search?q={searchTerm}&page=1
        [HttpGet("simple-search")]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<AwardNoticeDto>>>> SimpleSearch(
            [FromQuery] string q,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<PaginatedResponse<AwardNoticeDto>>
                    {
                        Success = false,
                        Message = "Search term is required"
                    });
                }

                var awardNotices = await _scraperService.SearchAwardNoticesByStringAsync(q, page, pageSize);

                return Ok(new ApiResponse<PaginatedResponse<AwardNoticeDto>>
                {
                    Success = true,
                    Message = $"Found {awardNotices.TotalCount} award notices matching '{q}'",
                    Data = awardNotices,
                    TotalCount = awardNotices.TotalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in simple search for award notices: '{q}'");
                return StatusCode(500, new ApiResponse<PaginatedResponse<AwardNoticeDto>>
                {
                    Success = false,
                    Message = $"Search error: {ex.Message}"
                });
            }
        }
    }
}