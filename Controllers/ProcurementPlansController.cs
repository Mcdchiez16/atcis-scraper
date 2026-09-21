using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
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
    [AllowAnonymous]
    [ApiExplorerSettings(GroupName = "scraping")]
    public class ProcurementPlansController : ControllerBase
    {
        private readonly ITenderScraperService _scraperService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ProcurementPlansController> _logger;
        private readonly HttpClient _httpClient;
        private readonly IStringMatchingService _stringMatchingService;

        public ProcurementPlansController(
            ITenderScraperService scraperService,
            IMemoryCache cache,
            ILogger<ProcurementPlansController> logger,
            HttpClient httpClient,
            IStringMatchingService stringMatchingService)
        {
            _scraperService = scraperService;
            _cache = cache;
            _logger = logger;
            _httpClient = httpClient;
            _stringMatchingService = stringMatchingService;
        }

        // GET: api/procurement-plans/page/{pageNumber}
        [HttpGet("page/{pageNumber}")]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>>> GetPage(int pageNumber)
        {
            try
            {
                var cacheKey = $"procurement_plans_page_{pageNumber}";
                if (!_cache.TryGetValue(cacheKey, out PaginatedResponse<AnnualProcurementPlanDto> plans))
                {
                    plans = await _scraperService.ScrapeAnnualProcurementPlansAsync(pageNumber);
                    _cache.Set(cacheKey, plans, TimeSpan.FromMinutes(10));
                }

                return Ok(new ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>
                {
                    Success = true,
                    Message = $"Successfully scraped annual procurement plans page {pageNumber}",
                    Data = plans,
                    TotalCount = plans.TotalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scraping annual procurement plans page {pageNumber}");
                return StatusCode(500, new ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>
                {
                    Success = false,
                    Message = $"Error scraping annual procurement plans page: {ex.Message}"
                });
            }
        }

        // GET: api/procurement-plans/pages?start=1&end=5
        [HttpGet("pages")]
        public async Task<ActionResult<ApiResponse<List<AnnualProcurementPlanDto>>>> GetMultiplePages(
            [FromQuery] int start = 1,
            [FromQuery] int end = 5)
        {
            try
            {
                if (start < 1 || end < start)
                {
                    return BadRequest(new ApiResponse<List<AnnualProcurementPlanDto>>
                    {
                        Success = false,
                        Message = "Invalid page range. Start must be >= 1 and end >= start"
                    });
                }

                var plans = await _scraperService.ScrapeMultipleAnnualProcurementPlanPagesAsync(start, end);

                return Ok(new ApiResponse<List<AnnualProcurementPlanDto>>
                {
                    Success = true,
                    Message = $"Successfully scraped annual procurement plan pages {start}-{end}",
                    Data = plans,
                    TotalCount = plans.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scraping annual procurement plan pages {start}-{end}");
                return StatusCode(500, new ApiResponse<List<AnnualProcurementPlanDto>>
                {
                    Success = false,
                    Message = $"Error scraping annual procurement plan pages: {ex.Message}"
                });
            }
        }

        // GET: api/procurement-plans/search
        [HttpGet("search")]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>>> Search(
            [FromQuery] string procuringEntity = null,
            [FromQuery] string year = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                // Create search request from query parameters
                var request = new AnnualProcurementPlanSearchRequest
                {
                    ProcuringEntity = procuringEntity,
                    Year = year,
                    Page = page,
                    PageSize = pageSize
                };

                _logger.LogInformation($"Searching annual procurement plans - Entity: {procuringEntity}, Year: {year}, Page: {page}");

                var plans = await _scraperService.SearchAnnualProcurementPlansAsync(request);

                return Ok(new ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>
                {
                    Success = true,
                    Message = "Successfully searched annual procurement plans",
                    Data = plans,
                    TotalCount = plans.TotalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching annual procurement plans");
                return StatusCode(500, new ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>
                {
                    Success = false,
                    Message = $"Error searching annual procurement plans: {ex.Message}"
                });
            }
        }

        // GET: api/procurement-plans/search-simple
        [HttpGet("search-simple")]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>>> SearchSimple(
            [FromQuery] string q = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>
                    {
                        Success = false,
                        Message = "Search term is required"
                    });
                }

                _logger.LogInformation($"Simple search for annual procurement plans: '{q}', page: {page}");

                // Check if the search term looks like a year
                bool isYearSearch = int.TryParse(q, out int searchYear) &&
                                   searchYear >= 2000 && searchYear <= DateTime.Now.Year + 2;

                // Check if the search term looks like an entity name
                bool isEntitySearch = q.Length > 3 && q.Contains(' ') || q.Contains("council", StringComparison.OrdinalIgnoreCase) ||
                                     q.Contains("authority", StringComparison.OrdinalIgnoreCase) ||
                                     q.Contains("board", StringComparison.OrdinalIgnoreCase) ||
                                     q.Contains("company", StringComparison.OrdinalIgnoreCase);

                var request = new AnnualProcurementPlanSearchRequest
                {
                    Page = page,
                    PageSize = pageSize
                };

                // Determine what type of search to do based on the input
                if (isYearSearch)
                {
                    request.Year = q;
                }
                else if (isEntitySearch)
                {
                    request.ProcuringEntity = q;
                }
                else
                {
                    // Try both
                    request.ProcuringEntity = q;
                    // Also try searching for the term in scraped data
                }

                var plans = await _scraperService.SearchAnnualProcurementPlansAsync(request);

                // If no results from structured search, try a broader search
                if (plans.Items.Count == 0)
                {
                    _logger.LogInformation($"No results from structured search, trying alternative approach");
                    // You might want to implement a fallback search method here
                }

                return Ok(new ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>
                {
                    Success = true,
                    Message = $"Found {plans.TotalCount} annual procurement plans matching '{q}'",
                    Data = plans,
                    TotalCount = plans.TotalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in simple search for annual procurement plans: '{q}'");
                return StatusCode(500, new ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }


        // GET: api/procurement-plans/detail?url={url}
        [HttpGet("detail")]
        public async Task<ActionResult<ApiResponse<AnnualProcurementPlanDetailDto>>> GetDetail(
            [FromQuery] string url)
        {
            try
            {
                if (string.IsNullOrEmpty(url))
                {
                    return BadRequest(new ApiResponse<AnnualProcurementPlanDetailDto>
                    {
                        Success = false,
                        Message = "URL is required"
                    });
                }

                var cacheKey = $"app_detail_{url.GetHashCode()}";
                if (!_cache.TryGetValue(cacheKey, out AnnualProcurementPlanDetailDto detail))
                {
                    detail = await _scraperService.GetAnnualProcurementPlanDetailAsync(url);
                    if (detail != null)
                    {
                        _cache.Set(cacheKey, detail, TimeSpan.FromHours(1));
                    }
                }

                return Ok(new ApiResponse<AnnualProcurementPlanDetailDto>
                {
                    Success = true,
                    Message = "Successfully retrieved annual procurement plan details",
                    Data = detail,
                    TotalCount = detail?.TotalItems ?? 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting annual procurement plan detail from {url}");
                return StatusCode(500, new ApiResponse<AnnualProcurementPlanDetailDto>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        private async Task<AnnualProcurementPlanDetailDto> GetAnnualProcurementPlanDetailAsync(string url)
        {
            try
            {
                var cacheKey = $"app_detail_{url.GetHashCode()}";
                if (_cache.TryGetValue(cacheKey, out AnnualProcurementPlanDetailDto cached))
                    return cached;

                var html = await _httpClient.GetStringAsync(url);
                var detail = ParseAnnualProcurementPlanDetail(html, url);

                _cache.Set(cacheKey, detail, TimeSpan.FromHours(1));
                return detail;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting annual procurement plan detail from {url}");
                throw;
            }
        }

        private AnnualProcurementPlanDetailDto ParseAnnualProcurementPlanDetail(string html, string url)
        {
            var detail = new AnnualProcurementPlanDetailDto
            {
                SourceUrl = url,
                Items = new List<ProcurementPlanItem>(),
                ScrapedAt = DateTime.UtcNow
            };

            try
            {
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(html);

                // Try to extract title and entity from the page
                var titleElement = htmlDoc.DocumentNode.SelectSingleNode("//h2[@class='box-title']");
                if (titleElement != null)
                {
                    detail.Title = titleElement.InnerText.Replace("View APP Details", "").Trim();
                }

                // Extract procuring entity and year from the URL or page
                var urlParts = url.Split('/');
                if (urlParts.Length > 1)
                {
                    detail.Year = urlParts.LastOrDefault();
                    detail.ProcuringEntity = urlParts.Reverse().Skip(1).FirstOrDefault();
                }

                // Parse the table with procurement items
                var table = htmlDoc.DocumentNode.SelectSingleNode("//table[@id='searchTable']");

                if (table != null)
                {
                    var rows = table.SelectNodes(".//tbody/tr");

                    if (rows != null)
                    {
                        foreach (var row in rows)
                        {
                            var cells = row.SelectNodes(".//td");

                            if (cells != null && cells.Count >= 20) // Check if we have enough cells
                            {
                                var item = new ProcurementPlanItem
                                {
                                    ItemId = cells[0].InnerText.Trim(),
                                    RefNo = cells[1].InnerText.Trim(),
                                    ClassOfProcurement = cells[2].InnerText.Trim(),
                                    ObjectCode = cells[3].InnerText.Trim(),
                                    Description = cells[4].InnerText.Trim(),
                                    PmoEndUser = cells[5].InnerText.Trim(),
                                    ProcurementMethod = cells[6].InnerText.Trim(),
                                    EoiPublicationDate = cells[7].InnerText.Trim(),
                                    EoiClosingDate = cells[8].InnerText.Trim(),
                                    TenderPublicationDate = cells[9].InnerText.Trim(),
                                    BidClosingDate = cells[10].InnerText.Trim(),
                                    AwardNoticeDate = cells[11].InnerText.Trim(),
                                    ContractSigningDate = cells[12].InnerText.Trim(),
                                    CycleDays = cells[13].InnerText.Trim(),
                                    LeadTime = cells[14].InnerText.Trim(),
                                    Spoc = cells[15].InnerText.Trim(),
                                    SourceOfFunds = cells[16].InnerText.Trim(),
                                    UnitOfMeasurement = cells[17].InnerText.Trim(),
                                    Quantity = cells[18].InnerText.Trim(),
                                    Comments = cells[19].InnerText.Trim(),
                                    IsSupplement = cells.Count > 20 ?
                                        cells[20].InnerText.Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase) : false
                                };

                                // Parse dates
                                item.ParsedTenderPublicationDate = ParseDate(item.TenderPublicationDate);
                                item.ParsedBidClosingDate = ParseDate(item.BidClosingDate);
                                item.ParsedAwardNoticeDate = ParseDate(item.AwardNoticeDate);
                                item.ParsedContractSigningDate = ParseDate(item.ContractSigningDate);

                                // Calculate estimated value if possible
                                if (decimal.TryParse(item.Quantity, out decimal quantity) &&
                                    !string.IsNullOrWhiteSpace(item.UnitOfMeasurement))
                                {
                                    // You might want to add unit price calculation here
                                    // For now, we'll just add the quantity as estimated value
                                    detail.TotalEstimatedValue += quantity;
                                }

                                detail.Items.Add(item);
                            }
                        }
                    }
                }

                detail.TotalItems = detail.Items.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error parsing annual procurement plan detail from {url}");
                // Return empty detail object with error information
                detail.Title = "Error parsing document";
            }

            return detail;
        }

        private DateTime? ParseDate(string dateString)
        {
            if (string.IsNullOrWhiteSpace(dateString) ||
                dateString.Equals("Not Applicable", StringComparison.OrdinalIgnoreCase))
                return null;

            if (DateTime.TryParseExact(dateString, "dd-MMM-yyyy",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out DateTime result))
            {
                return result;
            }

            return null;
        }

        // GET: api/procurement-plans/fuzzy-search?q=university of zimbabwe
        [HttpGet("fuzzy-search")]
        public async Task<ActionResult<ApiResponse<List<AnnualProcurementPlanDetailDto>>>> FuzzySearch(
            [FromQuery] string q = null,
            [FromQuery] int maxResults = 3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<List<AnnualProcurementPlanDetailDto>>
                    {
                        Success = false,
                        Message = "Search term is required"
                    });
                }

                _logger.LogInformation($"Fuzzy searching for annual procurement plans: '{q}'");

                // First, get all available procurement plans
                var allPlans = await GetAllProcurementPlansAsync();

                if (!allPlans.Any())
                {
                    return Ok(new ApiResponse<List<AnnualProcurementPlanDetailDto>>
                    {
                        Success = true,
                        Message = "No procurement plans found",
                        Data = new List<AnnualProcurementPlanDetailDto>(),
                        TotalCount = 0
                    });
                }

                // Find best matches using fuzzy matching
                var bestMatches = _stringMatchingService.FindBestMatches(allPlans, q, maxResults);

                if (!bestMatches.Any())
                {
                    return Ok(new ApiResponse<List<AnnualProcurementPlanDetailDto>>
                    {
                        Success = true,
                        Message = $"No close matches found for '{q}'",
                        Data = new List<AnnualProcurementPlanDetailDto>(),
                        TotalCount = 0
                    });
                }

                // Get details for each match
                var results = new List<AnnualProcurementPlanDetailDto>();

                foreach (var match in bestMatches)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(match.ViewAppUrl))
                        {
                            var detail = await GetAnnualProcurementPlanDetailAsync(match.ViewAppUrl);
                            results.Add(detail);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to get details for {match.ProcuringEntity}");
                        // Continue with other matches
                    }
                }

                return Ok(new ApiResponse<List<AnnualProcurementPlanDetailDto>>
                {
                    Success = true,
                    Message = $"Found {results.Count} close matches for '{q}'",
                    Data = results,
                    TotalCount = results.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in fuzzy search for annual procurement plans: '{q}'");
                return StatusCode(500, new ApiResponse<List<AnnualProcurementPlanDetailDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/procurement-plans/fuzzy-search-detailed?q=university of zimbabwe
        [HttpGet("fuzzy-search-detailed")]
        public async Task<ActionResult<ApiResponse<object>>> FuzzySearchDetailed(
            [FromQuery] string q = null,
            [FromQuery] int maxResults = 5,
            [FromQuery] double minScore = 0.3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<object>
                    {
                        Success = false,
                        Message = "Search term is required"
                    });
                }

                _logger.LogInformation($"Detailed fuzzy search for: '{q}'");

                // Get all available procurement plans
                var allPlans = await GetAllProcurementPlansAsync();

                if (!allPlans.Any())
                {
                    return Ok(new ApiResponse<object>
                    {
                        Success = true,
                        Message = "No procurement plans found",
                        Data = new
                        {
                            SearchTerm = q,
                            Matches = new List<object>(),
                            TotalPlansScanned = 0,
                            MatchStatistics = new
                            {
                                TotalMatches = 0,
                                AverageScore = 0.0,
                                BestScore = 0.0
                            }
                        },
                        TotalCount = 0
                    });
                }

                // Score all plans
                var scoredPlans = allPlans.Select(plan =>
                {
                    double entityScore = _stringMatchingService.CalculateFuzzyMatchScore(q, plan.ProcuringEntity);
                    double yearScore = _stringMatchingService.CalculateFuzzyMatchScore(q, plan.Year);
                    double combinedScore = (entityScore * 0.8) + (yearScore * 0.2);

                    return new
                    {
                        Plan = plan,
                        Score = combinedScore,
                        EntityScore = entityScore,
                        YearScore = yearScore,
                        IsExactMatch = plan.ProcuringEntity.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                                      plan.Year.Equals(q, StringComparison.OrdinalIgnoreCase)
                    };
                })
                .Where(x => x.Score >= minScore)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.IsExactMatch)
                .Take(maxResults)
                .ToList();

                // Get details for top matches
                var detailedResults = new List<object>();

                foreach (var scoredPlan in scoredPlans)
                {
                    try
                    {
                        if (!string.IsNullOrEmpty(scoredPlan.Plan.ViewAppUrl))
                        {
                            var detail = await GetAnnualProcurementPlanDetailAsync(scoredPlan.Plan.ViewAppUrl);

                            detailedResults.Add(new
                            {
                                MatchInfo = new
                                {
                                    ProcuringEntity = scoredPlan.Plan.ProcuringEntity,
                                    Year = scoredPlan.Plan.Year,
                                    OverallScore = Math.Round(scoredPlan.Score, 3),
                                    EntityScore = Math.Round(scoredPlan.EntityScore, 3),
                                    YearScore = Math.Round(scoredPlan.YearScore, 3),
                                    IsExactMatch = scoredPlan.IsExactMatch,
                                    SourceUrl = scoredPlan.Plan.ViewAppUrl
                                },
                                ProcurementPlan = detail
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Failed to get details for {scoredPlan.Plan.ProcuringEntity}");
                    }
                }

                // Calculate statistics
                var matchStats = scoredPlans.Any() ? new
                {
                    TotalMatches = scoredPlans.Count,
                    AverageScore = Math.Round(scoredPlans.Average(x => x.Score), 3),
                    BestScore = Math.Round(scoredPlans.Max(x => x.Score), 3),
                    ScoreDistribution = scoredPlans.GroupBy(x => Math.Floor(x.Score * 10) / 10)
                                                  .Select(g => new
                                                  {
                                                      ScoreRange = $"{g.Key:0.0}-{g.Key + 0.1:0.0}",
                                                      Count = g.Count()
                                                  })
                                                  .ToList()
                } : null;

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = $"Found {scoredPlans.Count} matches for '{q}' (scanned {allPlans.Count} plans)",
                    Data = new
                    {
                        SearchTerm = q,
                        Matches = detailedResults,
                        TotalPlansScanned = allPlans.Count,
                        MatchStatistics = matchStats,
                        SearchParameters = new
                        {
                            MaxResults = maxResults,
                            MinimumScore = minScore
                        }
                    },
                    TotalCount = detailedResults.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in detailed fuzzy search for: '{q}'");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // Helper method to get all procurement plans
        private async Task<List<AnnualProcurementPlanDto>> GetAllProcurementPlansAsync()
        {
            try
            {
                var cacheKey = "all_procurement_plans";
                if (!_cache.TryGetValue(cacheKey, out List<AnnualProcurementPlanDto> allPlans))
                {
                    allPlans = new List<AnnualProcurementPlanDto>();

                    // Scrape multiple pages (adjust range as needed)
                    int startPage = 1;
                    int endPage = 10; // You might want to scrape more pages

                    var scrapedPlans = await _scraperService.ScrapeMultipleAnnualProcurementPlanPagesAsync(startPage, endPage);

                    if (scrapedPlans != null)
                    {
                        allPlans.AddRange(scrapedPlans);
                    }

                    // Cache for a reasonable time
                    _cache.Set(cacheKey, allPlans, TimeSpan.FromHours(6));
                }

                return allPlans ?? new List<AnnualProcurementPlanDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all procurement plans");
                return new List<AnnualProcurementPlanDto>();
            }
        }
    }
}