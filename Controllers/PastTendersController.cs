using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Models;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PastTendersController : ControllerBase
    {
        private readonly ITenderScraperService _scraperService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<PastTendersController> _logger;

        public PastTendersController(
            ITenderScraperService scraperService,
            IMemoryCache cache,
            ILogger<PastTendersController> logger)
        {
            _scraperService = scraperService;
            _cache = cache;
            _logger = logger;
        }

        // GET: api/past-tenders/page/{pageNumber}
        [HttpGet("page/{pageNumber}")]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<Tender>>>> GetPage(int pageNumber)
        {
            try
            {
                var cacheKey = $"past_tenders_page_{pageNumber}";
                if (!_cache.TryGetValue(cacheKey, out PaginatedResponse<Tender> pastTenders))
                {
                    pastTenders = await _scraperService.ScrapePastTendersAsync(pageNumber);
                    _cache.Set(cacheKey, pastTenders, TimeSpan.FromMinutes(10));
                }

                return Ok(new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = true,
                    Message = $"Successfully scraped past tenders page {pageNumber}",
                    Data = pastTenders,
                    TotalCount = pastTenders.TotalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scraping past tenders page {pageNumber}");
                return StatusCode(500, new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = false,
                    Message = $"Error scraping past tenders page: {ex.Message}"
                });
            }
        }

        // GET: api/past-tenders/pages?start=1&end=5
        [HttpGet("pages")]
        public async Task<ActionResult<ApiResponse<List<Tender>>>> GetMultiplePages(
            [FromQuery] int start = 1,
            [FromQuery] int end = 5)
        {
            try
            {
                if (start < 1 || end < start)
                {
                    return BadRequest(new ApiResponse<List<Tender>>
                    {
                        Success = false,
                        Message = "Invalid page range. Start must be >= 1 and end >= start"
                    });
                }

                var pastTenders = await _scraperService.ScrapeMultiplePastTenderPagesAsync(start, end);

                return Ok(new ApiResponse<List<Tender>>
                {
                    Success = true,
                    Message = $"Successfully scraped past tender pages {start}-{end}",
                    Data = pastTenders,
                    TotalCount = pastTenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scraping past tender pages {start}-{end}");
                return StatusCode(500, new ApiResponse<List<Tender>>
                {
                    Success = false,
                    Message = $"Error scraping past tender pages: {ex.Message}"
                });
            }
        }

        // Add this to your PastTendersController

        // GET: api/past-tenders/simple-search?q=construction&page=1
        [HttpGet("simple-search")]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<Tender>>>> SimpleSearch(
            [FromQuery] string q,
            [FromQuery] int page = 1,
            [FromQuery] int maxPages = 10,
            [FromQuery] bool advanced = true)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<PaginatedResponse<Tender>>
                    {
                        Success = false,
                        Message = "Search term is required"
                    });
                }

                var request = new ScrapeRequest
                {
                    SearchString = q,
                    StartPage = page,
                    MaxPagesToSearch = maxPages,
                    UseAdvancedSearch = advanced
                };

                var pastTenders = await _scraperService.SearchPastTendersAsync(request);

                return Ok(new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = true,
                    Message = $"Found {pastTenders.TotalCount} past tenders matching '{q}'",
                    Data = pastTenders,
                    TotalCount = pastTenders.TotalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in simple search for past tenders: '{q}'");
                return StatusCode(500, new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = false,
                    Message = $"Search error: {ex.Message}"
                });
            }
        }

        // GET: api/past-tenders/search-by-entity?entity=ministry&q=roads
        [HttpGet("search-by-entity")]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<Tender>>>> SearchByEntity(
            [FromQuery] string entity,
            [FromQuery] string q,
            [FromQuery] int page = 1)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q) && string.IsNullOrWhiteSpace(entity))
                {
                    return BadRequest(new ApiResponse<PaginatedResponse<Tender>>
                    {
                        Success = false,
                        Message = "Either search term or entity is required"
                    });
                }

                var request = new ScrapeRequest
                {
                    SearchString = q,
                    StartPage = page
                };

                if (!string.IsNullOrWhiteSpace(entity))
                {
                    request.FilterEntities = new List<string> { entity };
                }

                var pastTenders = await _scraperService.SearchPastTendersAsync(request);

                var message = string.IsNullOrWhiteSpace(entity)
                    ? $"Found {pastTenders.TotalCount} past tenders matching '{q}'"
                    : $"Found {pastTenders.TotalCount} past tenders from '{entity}' matching '{q}'";

                return Ok(new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = true,
                    Message = message,
                    Data = pastTenders,
                    TotalCount = pastTenders.TotalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching past tenders by entity: '{entity}', query: '{q}'");
                return StatusCode(500, new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = false,
                    Message = $"Search error: {ex.Message}"
                });
            }
        }

        // POST: api/past-tenders/search
        [HttpPost("search")]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<Tender>>>> Search(
            [FromBody] ScrapeRequest request)
        {
            try
            {
                // Set defaults if not provided
                request.StartPage = request.StartPage <= 0 ? 1 : request.StartPage;
                request.MaxPagesToSearch = request.MaxPagesToSearch <= 0 ? 10 : request.MaxPagesToSearch;

                var pastTenders = await _scraperService.SearchPastTendersAsync(request);

                var message = string.IsNullOrEmpty(request.SearchString)
                    ? "Successfully searched past tenders with filters"
                    : $"Found {pastTenders.TotalCount} past tenders matching '{request.SearchString}'";

                return Ok(new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = true,
                    Message = message,
                    Data = pastTenders,
                    TotalCount = pastTenders.TotalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching past tenders");
                return StatusCode(500, new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = false,
                    Message = $"Error searching past tenders: {ex.Message}"
                });
            }
        }
    }
}