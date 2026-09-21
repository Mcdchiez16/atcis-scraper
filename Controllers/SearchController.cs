using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "EmployeeAccess")]
    [ApiExplorerSettings(GroupName = "scraping")]
    public class SearchController : ControllerBase
    {
        private readonly ITenderScraperService _scraperService;
        private readonly ILogger<SearchController> _logger;

        public SearchController(
            ITenderScraperService scraperService,
            ILogger<SearchController> logger)
        {
            _scraperService = scraperService;
            _logger = logger;
        }

        // GET: api/search/unified?q={searchstring}&page=1&entityType=all
        [HttpGet("unified")]
        public async Task<ActionResult<ApiResponse<object>>> UnifiedSearch(
            [FromQuery] string q,
            [FromQuery] int page = 1,
            [FromQuery] string entityType = "all")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Search query is required"
                    });
                }

                var results = new Dictionary<string, object>();

                // Search current tenders
                if (entityType == "all" || entityType == "tenders")
                {
                    var tenderResults = await _scraperService.SearchTendersAsync(q, page);
                    results["tenders"] = new
                    {
                        items = tenderResults.Tenders,
                        totalCount = tenderResults.TotalTenders,
                        hasMore = tenderResults.HasMorePages
                    };
                }

                // Search award notices
                if (entityType == "all" || entityType == "awards")
                {
                    var awardRequest = new AwardNoticeSearchRequest
                    {
                        AwardTitle = q,
                        Page = page,
                        PageSize = 20
                    };
                    var awardResults = await _scraperService.SearchAwardNoticesAsync(awardRequest);
                    results["awardNotices"] = awardResults;
                }

                // Search annual procurement plans
                if (entityType == "all" || entityType == "plans")
                {
                    var planRequest = new AnnualProcurementPlanSearchRequest
                    {
                        ProcuringEntity = q,
                        Page = page,
                        PageSize = 20
                    };
                    var planResults = await _scraperService.SearchAnnualProcurementPlansAsync(planRequest);
                    results["annualPlans"] = planResults;
                }

                // Search past tenders
                if (entityType == "all" || entityType == "past")
                {
                    var pastRequest = new ScrapeRequest
                    {
                        SearchNoticeTitle = q,
                        StartPage = page,
                        EndPage = page
                    };
                    var pastResults = await _scraperService.SearchPastTendersAsync(pastRequest);
                    results["pastTenders"] = pastResults;
                }

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = $"Search results for '{q}'",
                    Data = results,
                    TotalCount = results.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in unified search for '{q}'");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Search error: {ex.Message}"
                });
            }
        }
    }
}