using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Models;
using ZimbabweTenderAPI.Services;
using ZimbabweTenderAPI.Data;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "EmployeeAccess")]
    [ApiExplorerSettings(GroupName = "scraping")]
    public class TendersController : ControllerBase
    {
        private readonly ITenderScraperService _scraperService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TendersController> _logger;
        private readonly TenderSimilarityService _similarityService;

        public TendersController(
            ITenderScraperService scraperService,
            IMemoryCache cache,
            ILogger<TendersController> logger,
            TenderSimilarityService similarityService)
        {
            _scraperService = scraperService;
            _cache = cache;
            _logger = logger;
            _similarityService = similarityService;
        }

        // GET: api/tenders/page/{pageNumber}
        [HttpGet("page/{pageNumber}")]
        public async Task<ActionResult<ApiResponse<TenderBatch>>> GetPage(int pageNumber)
        {
            try
            {
                var cacheKey = $"page_{pageNumber}";
                if (!_cache.TryGetValue(cacheKey, out TenderBatch batch))
                {
                    batch = await _scraperService.ScrapePageAsync(pageNumber);
                    _cache.Set(cacheKey, batch, TimeSpan.FromMinutes(10));
                }

                return Ok(new ApiResponse<TenderBatch>
                {
                    Success = true,
                    Message = $"Successfully scraped page {pageNumber}",
                    Data = batch,
                    TotalCount = batch.TotalTenders
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scraping page {pageNumber}");
                return StatusCode(500, new ApiResponse<TenderBatch>
                {
                    Success = false,
                    Message = $"Error scraping page: {ex.Message}"
                });
            }
        }

        // GET: api/tenders/pages?start=1&end=5
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

                var tenders = await _scraperService.ScrapeMultiplePagesAsync(start, end);

                return Ok(new ApiResponse<List<Tender>>
                {
                    Success = true,
                    Message = $"Successfully scraped pages {start}-{end}",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scraping pages {start}-{end}");
                return StatusCode(500, new ApiResponse<List<Tender>>
                {
                    Success = false,
                    Message = $"Error scraping pages: {ex.Message}"
                });
            }
        }

        // POST: api/tenders/scrape
        [HttpPost("scrape")]
        public async Task<ActionResult<ApiResponse<List<Tender>>>> ScrapeTenders(
            [FromBody] ScrapeRequest request)
        {
            try
            {
                _logger.LogInformation($"Scrape request received: {request.StartPage} to {request.EndPage}");

                var tenders = await _scraperService.ScrapeMultiplePagesAsync(
                    request.StartPage,
                    request.EndPage);

                // Apply filters if specified
                if (request.FilterEntities?.Any() == true)
                {
                    tenders = tenders.Where(t =>
                        request.FilterEntities.Any(e =>
                            t.ProcuringEntity?.Contains(e, StringComparison.OrdinalIgnoreCase) ?? false))
                        .ToList();
                }

                if (request.FilterCategories?.Any() == true)
                {
                    tenders = tenders.Where(t =>
                        t.CategoryNames.Any(cn =>
                            request.FilterCategories.Any(fc =>
                                cn.Contains(fc, StringComparison.OrdinalIgnoreCase))))
                        .ToList();
                }

                return Ok(new ApiResponse<List<Tender>>
                {
                    Success = true,
                    Message = $"Successfully scraped {tenders.Count} tenders",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in scrape endpoint");
                return StatusCode(500, new ApiResponse<List<Tender>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/tenders/search?q=construction
        [HttpGet("search")]
        public async Task<ActionResult<ApiResponse<TenderBatch>>> Search(
            [FromQuery] string q,
            [FromQuery] int page = 1)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<TenderBatch>
                    {
                        Success = false,
                        Message = "Search query is required"
                    });
                }

                var results = await _scraperService.SearchTendersAsync(q, page);

                return Ok(new ApiResponse<TenderBatch>
                {
                    Success = true,
                    Message = $"Found {results.TotalTenders} tenders matching '{q}'",
                    Data = results,
                    TotalCount = results.TotalTenders
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching for '{q}'");
                return StatusCode(500, new ApiResponse<TenderBatch>
                {
                    Success = false,
                    Message = $"Search error: {ex.Message}"
                });
            }
        }

        // GET: api/tenders/entity/{entityName}
        [HttpGet("entity/{entityName}")]
        public async Task<ActionResult<ApiResponse<List<Tender>>>> GetByEntity(string entityName)
        {
            try
            {
                var tenders = await _scraperService.GetTendersByEntityAsync(entityName);

                return Ok(new ApiResponse<List<Tender>>
                {
                    Success = true,
                    Message = $"Found {tenders.Count} tenders from {entityName}",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting tenders for entity {entityName}");
                return StatusCode(500, new ApiResponse<List<Tender>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/tenders/closing-soon?days=7
        [HttpGet("closing-soon")]
        public async Task<ActionResult<ApiResponse<List<Tender>>>> GetClosingSoon(
            [FromQuery] int days = 7)
        {
            try
            {
                var tenders = await _scraperService.GetClosingSoonTendersAsync(days);

                return Ok(new ApiResponse<List<Tender>>
                {
                    Success = true,
                    Message = $"Found {tenders.Count} tenders closing in the next {days} days",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting closing soon tenders");
                return StatusCode(500, new ApiResponse<List<Tender>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/tenders/search-by-category?category={categoryName}&threshold={threshold}
        [HttpGet("search-by-category")]
        public async Task<ActionResult<ApiResponse<List<TenderWithSimilarityDto>>>> SearchByCategory(
            [FromQuery] string category,
            [FromQuery] double threshold = 0.7,
            [FromQuery] int limit = 50)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    return BadRequest(new ApiResponse<List<TenderWithSimilarityDto>>
                    {
                        Success = false,
                        Message = "Category name is required"
                    });
                }

                if (threshold < 0.1 || threshold > 1.0)
                {
                    return BadRequest(new ApiResponse<List<TenderWithSimilarityDto>>
                    {
                        Success = false,
                        Message = "Similarity threshold must be between 0.1 and 1.0"
                    });
                }

                _logger.LogInformation($"Searching tenders by category similarity: {category} (threshold: {threshold})");

                var similarTenders = await _scraperService.FindTendersByCategoryWithSimilarityAsync(category, threshold);

                // Filter by threshold and limit results
                var filteredResults = similarTenders
                    .Where(t => t.SimilarityScore >= threshold)
                    .Take(limit)
                    .ToList();

                return Ok(new ApiResponse<List<TenderWithSimilarityDto>>
                {
                    Success = true,
                    Message = $"Found {filteredResults.Count} tenders with category similarity ≥{threshold:P0} to '{category}'",
                    Data = filteredResults,
                    TotalCount = filteredResults.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching tenders by category: {category}");
                return StatusCode(500, new ApiResponse<List<TenderWithSimilarityDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/tenders/by-category/{categoryName}
        [HttpGet("by-category/{categoryName}")]
        public async Task<ActionResult<ApiResponse<List<Tender>>>> GetByCategory(string categoryName)
        {
            try
            {
                _logger.LogInformation($"Getting tenders by category: {categoryName}");

                // Get all tenders
                var allTenders = new List<Tender>();
                for (int page = 1; page <= 5; page++) // Limit to 5 pages for performance
                {
                    var batch = await _scraperService.ScrapePageAsync(page);
                    var categoryTenders = batch.Tenders.Where(t =>
                        t.CategoryNames?.Any(cn =>
                            cn.Contains(categoryName, StringComparison.OrdinalIgnoreCase)) == true
                    ).ToList();
                    allTenders.AddRange(categoryTenders);

                    await Task.Delay(100); // Small delay between requests
                }

                // Also check past tenders
                try
                {
                    var pastTenders = await _scraperService.ScrapeMultiplePastTenderPagesAsync(1, 2);
                    var categoryPastTenders = pastTenders.Where(t =>
                        t.CategoryNames?.Any(cn =>
                            cn.Contains(categoryName, StringComparison.OrdinalIgnoreCase)) == true
                    ).ToList();
                    allTenders.AddRange(categoryPastTenders);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch past tenders for category search");
                }

                // Remove duplicates
                allTenders = allTenders
                    .GroupBy(t => t.TenderId)
                    .Select(g => g.First())
                    .OrderByDescending(t => t.PublishDate ?? DateTime.MinValue)
                    .ToList();

                return Ok(new ApiResponse<List<Tender>>
                {
                    Success = true,
                    Message = $"Found {allTenders.Count} tenders in category '{categoryName}'",
                    Data = allTenders,
                    TotalCount = allTenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting tenders by category: {categoryName}");
                return StatusCode(500, new ApiResponse<List<Tender>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/tenders/search-similar-categories?category={categoryName}
        [HttpGet("search-similar-categories")]
        public async Task<ActionResult<ApiResponse<List<CategorySimilarityDto>>>> SearchSimilarCategories(
            [FromQuery] string category,
            [FromQuery] double threshold = 0.6)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(category))
                {
                    return BadRequest(new ApiResponse<List<CategorySimilarityDto>>
                    {
                        Success = false,
                        Message = "Category name is required"
                    });
                }

                _logger.LogInformation($"Searching for categories similar to: {category}");

                var allCategories = await GetAllUniqueCategoriesAsync();
                var similarCategories = new List<CategorySimilarityDto>();

                foreach (var cat in allCategories)
                {
                    var similarity = _similarityService.CalculateCategoryNameSimilarity(cat, category);
                    if (similarity >= threshold)
                    {
                        similarCategories.Add(new CategorySimilarityDto
                        {
                            CategoryName = cat,
                            SimilarityScore = similarity,
                            TargetCategory = category
                        });
                    }
                }

                var sortedResults = similarCategories
                    .OrderByDescending(c => c.SimilarityScore)
                    .ToList();

                return Ok(new ApiResponse<List<CategorySimilarityDto>>
                {
                    Success = true,
                    Message = $"Found {sortedResults.Count} categories similar to '{category}' (≥{threshold:P0})",
                    Data = sortedResults,
                    TotalCount = sortedResults.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching similar categories: {category}");
                return StatusCode(500, new ApiResponse<List<CategorySimilarityDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/tenders/categories
        [HttpGet("categories")]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<CategoryStatsDto>>>> GetAllCategories(
            [FromQuery] bool includeStats = false,
            [FromQuery] int minTenderCount = 0,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                _logger.LogInformation("Getting all unique categories");

                var cacheKey = $"all_categories_{page}_{pageSize}_{includeStats}_{minTenderCount}";

                if (includeStats)
                {
                    // When includeStats=true, return CategoryStatsDto objects
                    if (!_cache.TryGetValue(cacheKey, out List<CategoryStatsDto> categoriesWithStats))
                    {
                        categoriesWithStats = await GetCategoriesWithStatsAsync();

                        // Apply filtering by tender count
                        var filteredCategories = categoriesWithStats
                            .Where(c => c.TenderCount >= minTenderCount)
                            .OrderByDescending(c => c.TenderCount)
                            .ToList();

                        categoriesWithStats = filteredCategories;

                        _cache.Set(cacheKey, categoriesWithStats, TimeSpan.FromMinutes(30));
                    }

                    // Apply pagination
                    var totalCount = categoriesWithStats.Count;
                    var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                    var pagedCategories = categoriesWithStats
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();

                    var paginatedResponse = new PaginatedResponse<CategoryStatsDto>
                    {
                        Items = pagedCategories,
                        PageNumber = page,
                        PageSize = pageSize,
                        TotalPages = totalPages,
                        TotalCount = totalCount,
                        HasPreviousPage = page > 1,
                        HasNextPage = page < totalPages
                    };

                    var response = new ApiResponse<PaginatedResponse<CategoryStatsDto>>
                    {
                        Success = true,
                        Message = $"Found {totalCount} unique categories with statistics. Page {page} of {totalPages}.",
                        Data = paginatedResponse,
                        TotalCount = totalCount
                    };

                    return Ok(response);
                }
                else
                {
                    // When includeStats=false, return just category names
                    if (!_cache.TryGetValue(cacheKey, out List<string> allCategories))
                    {
                        allCategories = await GetAllUniqueCategoriesAsync();

                        // Apply filtering by tender count if requested
                        if (minTenderCount > 0)
                        {
                            var categoriesWithStats = await GetCategoriesWithStatsAsync();
                            var filteredCategories = categoriesWithStats
                                .Where(c => c.TenderCount >= minTenderCount)
                                .OrderByDescending(c => c.TenderCount)
                                .Select(c => c.CategoryName)
                                .ToList();

                            allCategories = filteredCategories;
                        }
                        else
                        {
                            // Sort alphabetically by default
                            allCategories = allCategories
                                .OrderBy(c => c)
                                .ToList();
                        }

                        _cache.Set(cacheKey, allCategories, TimeSpan.FromMinutes(30));
                    }

                    // Apply pagination
                    var totalCount = allCategories.Count;
                    var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                    var pagedCategories = allCategories
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();

                    var paginatedResponse = new PaginatedResponse<string>
                    {
                        Items = pagedCategories,
                        PageNumber = page,
                        PageSize = pageSize,
                        TotalPages = totalPages,
                        TotalCount = totalCount,
                        HasPreviousPage = page > 1,
                        HasNextPage = page < totalPages
                    };

                    var response = new ApiResponse<PaginatedResponse<string>>
                    {
                        Success = true,
                        Message = $"Found {totalCount} unique categories. Page {page} of {totalPages}.",
                        Data = paginatedResponse,
                        TotalCount = totalCount
                    };

                    return Ok(response);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all categories");
                return StatusCode(500, new ApiResponse<PaginatedResponse<string>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }
        // GET: api/tenders/categories-with-stats
        [HttpGet("categories-with-stats")]
        public async Task<ActionResult<ApiResponse<List<CategoryStatsDto>>>> GetCategoriesWithStats(
            [FromQuery] int minTenderCount = 1,
            [FromQuery] int limit = 100,
            [FromQuery] bool sortByCount = true)
        {
            try
            {
                _logger.LogInformation("Getting categories with statistics");

                var cacheKey = $"categories_stats_{minTenderCount}_{limit}_{sortByCount}";
                if (!_cache.TryGetValue(cacheKey, out List<CategoryStatsDto> categoriesWithStats))
                {
                    categoriesWithStats = await GetCategoriesWithStatsAsync();

                    // Apply filtering and sorting
                    var filteredCategories = categoriesWithStats
                        .Where(c => c.TenderCount >= minTenderCount)
                        .ToList();

                    if (sortByCount)
                    {
                        filteredCategories = filteredCategories
                            .OrderByDescending(c => c.TenderCount)
                            .ToList();
                    }
                    else
                    {
                        filteredCategories = filteredCategories
                            .OrderBy(c => c.CategoryName)
                            .ToList();
                    }

                    categoriesWithStats = filteredCategories
                        .Take(limit)
                        .ToList();

                    _cache.Set(cacheKey, categoriesWithStats, TimeSpan.FromMinutes(30));
                }

                return Ok(new ApiResponse<List<CategoryStatsDto>>
                {
                    Success = true,
                    Message = $"Found {categoriesWithStats.Count} categories with statistics",
                    Data = categoriesWithStats,
                    TotalCount = categoriesWithStats.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting categories with statistics");
                return StatusCode(500, new ApiResponse<List<CategoryStatsDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/tenders/categories-popular
        [HttpGet("categories-popular")]
        public async Task<ActionResult<ApiResponse<List<CategoryStatsDto>>>> GetPopularCategories(
            [FromQuery] int top = 20)
        {
            try
            {
                _logger.LogInformation($"Getting top {top} popular categories");

                var cacheKey = $"popular_categories_{top}";
                if (!_cache.TryGetValue(cacheKey, out List<CategoryStatsDto> popularCategories))
                {
                    var allCategoriesWithStats = await GetCategoriesWithStatsAsync();

                    popularCategories = allCategoriesWithStats
                        .OrderByDescending(c => c.TenderCount)
                        .Take(top)
                        .ToList();

                    // Calculate percentages
                    var totalTenders = popularCategories.Sum(c => c.TenderCount);
                    foreach (var category in popularCategories)
                    {
                        category.Percentage = totalTenders > 0
                            ? Math.Round((category.TenderCount / (double)totalTenders) * 100, 2)
                            : 0;
                    }

                    _cache.Set(cacheKey, popularCategories, TimeSpan.FromMinutes(30));
                }

                return Ok(new ApiResponse<List<CategoryStatsDto>>
                {
                    Success = true,
                    Message = $"Top {popularCategories.Count} most popular categories",
                    Data = popularCategories,
                    TotalCount = popularCategories.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting popular categories");
                return StatusCode(500, new ApiResponse<List<CategoryStatsDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // Helper method to get all unique categories
        private async Task<List<string>> GetAllUniqueCategoriesAsync()
        {
            var allCategories = new HashSet<string>();
            var allTenders = new List<Tender>();

            // Get current tenders
            for (int page = 1; page <= 5; page++)
            {
                var batch = await _scraperService.ScrapePageAsync(page);
                allTenders.AddRange(batch.Tenders);
            }

            // Get past tenders
            try
            {
                var pastTenders = await _scraperService.ScrapeMultiplePastTenderPagesAsync(1, 3);
                allTenders.AddRange(pastTenders);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch past tenders for category list");
            }

            // Extract all unique categories
            foreach (var tender in allTenders)
            {
                if (tender.CategoryNames != null)
                {
                    foreach (var category in tender.CategoryNames)
                    {
                        if (!string.IsNullOrWhiteSpace(category))
                            allCategories.Add(category.Trim());
                    }
                }
            }

            return allCategories.ToList();
        }

        // Helper method to get categories with statistics
        private async Task<List<CategoryStatsDto>> GetCategoriesWithStatsAsync()
        {
            var categoryCounts = new Dictionary<string, int>();
            var allTenders = new List<Tender>();

            // Get current tenders
            for (int page = 1; page <= 5; page++)
            {
                var batch = await _scraperService.ScrapePageAsync(page);
                allTenders.AddRange(batch.Tenders);
            }

            // Get past tenders
            try
            {
                var pastTenders = await _scraperService.ScrapeMultiplePastTenderPagesAsync(1, 3);
                allTenders.AddRange(pastTenders);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch past tenders for category statistics");
            }

            // Count tenders per category
            foreach (var tender in allTenders)
            {
                if (tender.CategoryNames != null)
                {
                    foreach (var category in tender.CategoryNames)
                    {
                        if (!string.IsNullOrWhiteSpace(category))
                        {
                            var trimmedCategory = category.Trim();
                            if (categoryCounts.ContainsKey(trimmedCategory))
                                categoryCounts[trimmedCategory]++;
                            else
                                categoryCounts[trimmedCategory] = 1;
                        }
                    }
                }
            }

            // Convert to DTOs
            var categoriesWithStats = categoryCounts
                .Select(kvp => new CategoryStatsDto
                {
                    CategoryName = kvp.Key,
                    TenderCount = kvp.Value
                })
                .ToList();

            return categoriesWithStats;
        }

        // GET: api/tenders/stats
        [HttpGet("stats")]
        public async Task<ActionResult<ApiResponse<object>>> GetStats()
        {
            try
            {
                var totalPages = await _scraperService.GetTotalPagesAsync();
                var firstPage = await _scraperService.ScrapePageAsync(1);

                // Get category statistics
                var categories = await GetAllUniqueCategoriesAsync();
                var categoriesWithStats = await GetCategoriesWithStatsAsync();
                var popularCategories = categoriesWithStats
                    .OrderByDescending(c => c.TenderCount)
                    .Take(10)
                    .ToList();

                var stats = new
                {
                    TotalPages = totalPages,
                    TendersPerPage = firstPage.TotalTenders,
                    EstimatedTotalTenders = totalPages * firstPage.TotalTenders,
                    TotalCategories = categories.Count,
                    TopCategories = popularCategories.Select(c => new
                    {
                        c.CategoryName,
                        c.TenderCount
                    }),
                    LastScraped = DateTime.UtcNow,
                    BaseUrl = "https://egp.praz.org.zw"
                };

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "System statistics",
                    Data = stats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting stats");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get full Zimbabwe live tender details, procurement parameters, line items table, and fee structure from PRAZ.
        /// </summary>
        /// <param name="tenderId">The PRAZ tender ID (e.g. 46190)</param>
        [HttpGet("details/{tenderId}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<ZimbabweTenderDetailDto>>> GetTenderDetails(
            string tenderId,
            [FromServices] ZimbabweTenderAPI.Data.ApplicationDbContext context)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tenderId))
                {
                    return BadRequest(new ApiResponse<ZimbabweTenderDetailDto>
                    {
                        Success = false,
                        Message = "tenderId parameter is required."
                    });
                }

                var cleanId = tenderId.Trim();
                var dbTender = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.FirstOrDefaultAsync(
                    context.LiveTenders,
                    l => l.TenderId == cleanId || l.Id.ToString() == cleanId || l.ReferenceNumber == cleanId);

                string targetTenderId = !string.IsNullOrWhiteSpace(dbTender?.TenderId) ? dbTender.TenderId : cleanId;
                var details = await _scraperService.GetZimbabweTenderDetailsAsync(targetTenderId);

                return Ok(new ApiResponse<ZimbabweTenderDetailDto>
                {
                    Success = true,
                    Message = $"Successfully retrieved tender details for {targetTenderId}",
                    Data = details
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Zimbabwe tender details for {TenderId}", tenderId);
                return StatusCode(500, new ApiResponse<ZimbabweTenderDetailDto>
                {
                    Success = false,
                    Message = $"Error retrieving tender details: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get attached documents for a Zimbabwe tender from PRAZ (requires session cookie if protected).
        /// </summary>
        /// <param name="tenderId">The PRAZ tender ID (e.g. 46190)</param>
        /// <param name="sessionCookie">Optional CAKEPHP or session cookie</param>
        [HttpGet("documents/{tenderId}")]
        public async Task<ActionResult<ApiResponse<List<ZimbabweTenderDocumentDto>>>> GetTenderDocuments(
            string tenderId,
            [FromQuery] string? sessionCookie = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(tenderId))
                {
                    return BadRequest(new ApiResponse<List<ZimbabweTenderDocumentDto>>
                    {
                        Success = false,
                        Message = "tenderId parameter is required."
                    });
                }

                var documents = await _scraperService.GetZimbabweTenderDocumentsAsync(tenderId, sessionCookie);

                return Ok(new ApiResponse<List<ZimbabweTenderDocumentDto>>
                {
                    Success = true,
                    Message = $"Retrieved {documents.Count} documents for tender {tenderId}",
                    Data = documents,
                    TotalCount = documents.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Zimbabwe tender documents for {TenderId}", tenderId);
                return StatusCode(500, new ApiResponse<List<ZimbabweTenderDocumentDto>>
                {
                    Success = false,
                    Message = $"Error retrieving documents: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Set or update the active PRAZ session cookie (e.g. CAKEPHP=...) for automated authenticated document downloads.
        /// </summary>
        [HttpPost("praz-session")]
        public ActionResult<ApiResponse<string>> SetPrazSession([FromBody] PrazSessionRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.SessionCookie))
                {
                    return BadRequest(new ApiResponse<string>
                    {
                        Success = false,
                        Message = "SessionCookie cannot be empty."
                    });
                }

                _scraperService.SetPrazSessionCookie(request.SessionCookie);

                return Ok(new ApiResponse<string>
                {
                    Success = true,
                    Message = "PRAZ session cookie updated successfully.",
                    Data = "Session active"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting PRAZ session cookie");
                return StatusCode(500, new ApiResponse<string>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Analyze a tender and its attached documents using Google Gemini AI to generate a comprehensive intelligence report.
        /// </summary>
        [HttpPost("analyze-document")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<TenderDocumentAnalysisResponse>>> AnalyzeDocument(
            [FromBody] TenderDocumentAnalysisRequest request,
            [FromServices] ApplicationDbContext context,
            [FromServices] IConfiguration config,
            [FromServices] IHttpClientFactory clientFactory)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.TenderId))
                {
                    return BadRequest(new ApiResponse<TenderDocumentAnalysisResponse>
                    {
                        Success = false,
                        Message = "TenderId is required."
                    });
                }

                var cleanId = request.TenderId.Trim();
                var targetTenderId = cleanId;

                // Lookup LiveTenders by Id, TenderId, or ReferenceNumber
                var liveTender = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                    .FirstOrDefaultAsync(context.LiveTenders, l => l.TenderId == cleanId || l.Id.ToString() == cleanId || l.ReferenceNumber == cleanId);

                if (liveTender != null && !string.IsNullOrWhiteSpace(liveTender.TenderId))
                {
                    targetTenderId = liveTender.TenderId;
                }

                // Scrape live details and gazetted items
                ZimbabweTenderDetailDto? tenderDetail = null;
                try
                {
                    tenderDetail = await _scraperService.GetZimbabweTenderDetailsAsync(targetTenderId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not scrape live details for {TenderId}", targetTenderId);
                }

                var tenderTitle = tenderDetail?.ProjectName ?? liveTender?.Title ?? "Procurement Tender";
                var entity = tenderDetail?.ProcuringEntity ?? liveTender?.ProcuringEntity ?? "Public Procuring Authority";
                var refNo = tenderDetail?.TenderReferenceNumber ?? liveTender?.ReferenceNumber ?? cleanId;
                var method = tenderDetail?.ProcurementMethod ?? liveTender?.Scope ?? "Standard Gazetted";
                var closing = tenderDetail?.ClosingDate?.ToString("yyyy-MM-dd HH:mm") ?? liveTender?.ClosingDate?.ToString("yyyy-MM-dd") ?? "Open";
                var location = tenderDetail?.DeliveryProjectLocation ?? "Zimbabwe";
                var validity = tenderDetail?.BidValidityPeriod ?? "90";
                var funding = tenderDetail?.FundingSource ?? "Government Statutory Treasury";

                // Format line items
                var itemsText = new System.Text.StringBuilder();
                if (tenderDetail?.LineItems != null && tenderDetail.LineItems.Count > 0)
                {
                    foreach (var item in tenderDetail.LineItems)
                    {
                        itemsText.AppendLine($"- Item {item.ItemNumber}: {item.LotName} | {item.LotDescription} | Qty: {item.Quantity} {item.UnitOfMeasure} | UNSPSC: {item.Unspsc}");
                    }
                }
                else
                {
                    itemsText.AppendLine("- Detailed specifications gazetted in official bidding dossier.");
                }

                var docName = request.FileName ?? tenderDetail?.Documents?.FirstOrDefault()?.FileName ?? "Tender Bidding Document";

                var prompt = $@"You are a Chief Public Procurement & Bidding Intelligence Expert specializing in Zimbabwe (PPDPA Act, PRAZ regulations) and African public procurement.

Analyze the following tender notice and bidding requirements in exhaustive detail:

TENDER METADATA:
- Title: {tenderTitle}
- Reference Number: {refNo}
- Procuring Authority: {entity}
- Procurement Method: {method}
- Bid Validity Period: {validity} days
- Closing Date: {closing}
- Delivery Location: {location}
- Funding Source: {funding}
- Document Under Analysis: {docName}

GAZETTED DELIVERABLES & REQUIREMENTS:
{itemsText}

USER QUESTION / FOCUS:
{(string.IsNullOrWhiteSpace(request.CustomPrompt) ? "Perform a comprehensive statutory bidding, evaluation, and win-strategy analysis." : request.CustomPrompt)}

Respond with a strictly formatted JSON object with NO surrounding markdown backticks or commentary matching this exact schema:
{{
  ""executiveSummary"": ""Detailed plain-English summary of what is being procured, project background, and key objectives (2-3 paragraphs)."",
  ""keyDeliverables"": [""Deliverable 1 with technical specs"", ""Deliverable 2 with quantities"", ""Deliverable 3 with SLA""],
  ""mandatoryChecklist"": [
    {{ ""item"": ""PRAZ Category Registration"", ""status"": ""Mandatory"", ""description"": ""Specific category registration for 2026 required"" }},
    {{ ""item"": ""ZIMRA Tax Clearance (ITF263)"", ""status"": ""Mandatory"", ""description"": ""Valid tax clearance certificate"" }},
    {{ ""item"": ""NSSA Social Security Clearance"", ""status"": ""Mandatory"", ""description"": ""Certificate of good standing"" }},
    {{ ""item"": ""Bid Security Bond / Declaration"", ""status"": ""Mandatory"", ""description"": ""Required bank guarantee or declaration"" }},
    {{ ""item"": ""Manufacturer Authorization Form (MAF)"", ""status"": ""Conditional"", ""description"": ""Required for authorized distributors"" }}
  ],
  ""evaluationMatrix"": [
    {{ ""criterion"": ""Preliminary Statutory Compliance"", ""weight"": ""Pass/Fail"", ""description"": ""All statutory certificates and forms"" }},
    {{ ""criterion"": ""Technical Specification & Methodology"", ""weight"": ""70 Points"", ""description"": ""Conformance to gazetted lot specifications"" }},
    {{ ""criterion"": ""Past Experience & Track Record"", ""weight"": ""20 Points"", ""description"": ""3 contactable reference letters for similar jobs"" }},
    {{ ""criterion"": ""Delivery Schedule & SLA"", ""weight"": ""10 Points"", ""description"": ""Timeline commitment and warranty terms"" }}
  ],
  ""commercialTerms"": {{
    ""currency"": ""USD / ZiG gazetted currency"",
    ""paymentTerms"": ""Standard government 30-day billing upon inspection and handover"",
    ""deliveryPeriod"": ""{tenderDetail?.DeliveryPeriod ?? "As stipulated in dossier"}"",
    ""warrantyPeriod"": ""12 to 24 Months Manufacturer Warranty with SLA"",
    ""penalties"": ""0.5% per week for delayed delivery up to 10% maximum""
  }},
  ""riskAssessment"": [
    {{ ""risk"": ""Strict Delivery Schedule"", ""severity"": ""Medium"", ""mitigation"": ""Ensure local stock availability or express freight arrangements."" }},
    {{ ""risk"": ""Bid Bond Non-Conformance"", ""severity"": ""High"", ""mitigation"": ""Obtain bond strictly in the gazetted format from a registered commercial bank."" }}
  ],
  ""requiredItems"": [
    {{
      ""itemNumber"": ""1"",
      ""itemName"": ""Exact title of required equipment/lot item"",
      ""specifications"": ""Detailed technical specifications, power ratings, capacities, dimensions, or deliverables"",
      ""quantity"": ""5"",
      ""unit"": ""Units / Lots / Meters / Packets"",
      ""estimatedUnitCost"": 2500.0,
      ""totalEstimatedCost"": 12500.0,
      ""complianceStandards"": ""IEC / ISO / SABS standard or PRAZ technical category"",
      ""suggestedSuppliers"": [
        {{
          ""supplierName"": ""Top rated regional or international supplier/OEM"",
          ""tier"": ""Tier 1 OEM Partner / PRAZ Registered Distributor"",
          ""matchScore"": 96,
          ""location"": ""Harare, Zimbabwe / SADC Regional Warehouse"",
          ""leadTime"": ""5-10 Days / Immediate Local Stock"",
          ""estimatedPrice"": 2100.0,
          ""contactEmail"": ""tenders@supplier.co.zw"",
          ""contactPhone"": ""+263 772 000 000"",
          ""prazRegistered"": true,
          ""notes"": ""Certified distributor offering local installation warranty and technical backup.""
        }}
      ]
    }}
  ],
  ""pricingStrategy"": ""Detailed strategic advice on pricing structure, margin optimization, and competitive positioning."",
  ""aiFitScore"": 92,
  ""bidDecision"": ""STRONG_BID""
}}";

                var apiKey = config["Gemini:ApiKey"] ?? "AIzaSyDATB5TBbinPMq-bjZgcDiYo3miJZVx0Cg";
                var modelName = config["Gemini:ModelName"] ?? "gemini-2.5-flash";
                var geminiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";

                var httpClient = clientFactory.CreateClient();
                var geminiRequest = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[] { new { text = prompt } }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.2,
                        maxOutputTokens = 8000,
                        responseMimeType = "application/json"
                    }
                };

                var httpContent = new System.Net.Http.StringContent(
                    System.Text.Json.JsonSerializer.Serialize(geminiRequest),
                    System.Text.Encoding.UTF8,
                    "application/json"
                );

                var geminiResponse = await httpClient.PostAsync(geminiUrl, httpContent);
                var geminiJson = await geminiResponse.Content.ReadAsStringAsync();

                if (!geminiResponse.IsSuccessStatusCode)
                {
                    _logger.LogError("Gemini API call failed: {StatusCode} - {Content}", geminiResponse.StatusCode, geminiJson);
                    return StatusCode(500, new ApiResponse<TenderDocumentAnalysisResponse>
                    {
                        Success = false,
                        Message = $"Gemini AI error: {geminiResponse.StatusCode}"
                    });
                }

                // Parse candidate text
                using var jsonDoc = System.Text.Json.JsonDocument.Parse(geminiJson);
                var rawText = jsonDoc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString() ?? "{}";

                // Clean backticks if any
                var cleanJson = rawText.Trim();
                if (cleanJson.StartsWith("```json")) cleanJson = cleanJson.Substring(7);
                if (cleanJson.StartsWith("```")) cleanJson = cleanJson.Substring(3);
                if (cleanJson.EndsWith("```")) cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
                cleanJson = cleanJson.Trim();

                var analysisResult = System.Text.Json.JsonSerializer.Deserialize<TenderDocumentAnalysisResponse>(cleanJson, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new TenderDocumentAnalysisResponse();

                analysisResult.TenderId = targetTenderId;
                analysisResult.TenderTitle = tenderTitle;
                analysisResult.ProcuringEntity = entity;
                analysisResult.FileName = docName;
                analysisResult.RawAiResponse = cleanJson;

                return Ok(new ApiResponse<TenderDocumentAnalysisResponse>
                {
                    Success = true,
                    Message = "Document analysis generated successfully by Gemini AI.",
                    Data = analysisResult
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AnalyzeDocument endpoint");
                return StatusCode(500, new ApiResponse<TenderDocumentAnalysisResponse>
                {
                    Success = false,
                    Message = $"Error analyzing document: {ex.Message}"
                });
            }
        }

        // Additional DTOs
        public class CategorySimilarityDto
        {
            public string CategoryName { get; set; }
            public double SimilarityScore { get; set; }
            public string TargetCategory { get; set; }
            public List<string> ExampleTenders { get; set; } = new List<string>();
        }

        public class CategoryStatsDto
        {
            public string CategoryName { get; set; }
            public int TenderCount { get; set; }
            public double Percentage { get; set; }
            public DateTime? LastTenderDate { get; set; }
        }
    }
}