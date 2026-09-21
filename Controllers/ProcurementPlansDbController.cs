using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    // ==================== PROCUREMENT PLANS DATABASE CONTROLLER ====================
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "GuestAccess")]
    [ApiExplorerSettings(GroupName = "database")]
    public class ProcurementPlansDbController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ProcurementPlansDbController> _logger;
        private readonly IStringMatchingService _stringMatchingService;

        public ProcurementPlansDbController(
            ApplicationDbContext context,
            ILogger<ProcurementPlansDbController> logger,
            IStringMatchingService stringMatchingService)
        {
            _context = context;
            _logger = logger;
            _stringMatchingService = stringMatchingService;
        }

        // GET: api/procurementplansdb
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string sortBy = "Entity")
        {
            try
            {
                var query = _context.ProcurementPlans.AsQueryable();

                // Apply sorting
                query = sortBy.ToLower() switch
                {
                    "entity" => query.OrderBy(p => p.ProcuringEntity),
                    "year" => query.OrderByDescending(p => p.Year),
                    "items" => query.OrderByDescending(p => p.TotalItems),
                    "value" => query.OrderByDescending(p => p.TotalEstimatedValue),
                    _ => query.OrderBy(p => p.ProcuringEntity)
                };

                var totalCount = await query.CountAsync();
                var entities = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var plans = entities.Select(MapToDto).ToList();

                return Ok(new ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>
                {
                    Success = true,
                    Message = $"Retrieved {plans.Count} procurement plans",
                    Data = new PaginatedResponse<AnnualProcurementPlanDto>
                    {
                        Items = plans,
                        PageNumber = page,
                        PageSize = pageSize,
                        TotalCount = totalCount,
                        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                        HasPreviousPage = page > 1,
                        HasNextPage = page < (int)Math.Ceiling(totalCount / (double)pageSize)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting plans");
                return StatusCode(500, new ApiResponse<PaginatedResponse<AnnualProcurementPlanDto>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // GET: api/procurementplansdb/{id}
        [HttpGet("{id:int}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<AnnualProcurementPlanDetailDto>>> GetById(int id)
        {
            try
            {
                var entity = await _context.ProcurementPlans
                    .Include(p => p.Items)
                    .FirstOrDefaultAsync(p => p.Id == id);

                if (entity == null)
                    return NotFound(new ApiResponse<AnnualProcurementPlanDetailDto>
                    {
                        Success = false,
                        Message = "Procurement plan not found"
                    });

                var detail = MapToDetailDto(entity);

                return Ok(new ApiResponse<AnnualProcurementPlanDetailDto>
                {
                    Success = true,
                    Data = detail
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting plan {id}");
                return StatusCode(500, new ApiResponse<AnnualProcurementPlanDetailDto>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // GET: api/procurementplansdb/all - Get ALL plans without pagination
        [HttpGet("all")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<AnnualProcurementPlanDto>>>> GetAllNoPagination(
            [FromQuery] string? year = null,
            [FromQuery] string? entity = null,
            [FromQuery] string sortBy = "Entity")
        {
            try
            {
                var query = _context.ProcurementPlans.AsQueryable();

                // Apply filters
                if (!string.IsNullOrWhiteSpace(year))
                    query = query.Where(p => p.Year == year);

                if (!string.IsNullOrWhiteSpace(entity))
                    query = query.Where(p => p.ProcuringEntity.Contains(entity));

                // Apply sorting
                query = sortBy.ToLower() switch
                {
                    "entity" => query.OrderBy(p => p.ProcuringEntity),
                    "year" => query.OrderByDescending(p => p.Year),
                    "items" => query.OrderByDescending(p => p.TotalItems),
                    "value" => query.OrderByDescending(p => p.TotalEstimatedValue),
                    _ => query.OrderBy(p => p.ProcuringEntity)
                };

                var entities = await query.ToListAsync();
                var plans = entities.Select(MapToDto).ToList();

                return Ok(new ApiResponse<List<AnnualProcurementPlanDto>>
                {
                    Success = true,
                    Message = $"Retrieved all {plans.Count} procurement plans",
                    Data = plans,
                    TotalCount = plans.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all plans");
                return StatusCode(500, new ApiResponse<List<AnnualProcurementPlanDto>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // GET: api/procurementplansdb/search - FUZZY SEARCH
        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<ProcurementPlanSearchResultDto>>>> Search(
            [FromQuery] string q,
            [FromQuery] int topN = 20,
            [FromQuery] double minScore = 0.3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<List<ProcurementPlanSearchResultDto>>
                    {
                        Success = false,
                        Message = "Search query is required"
                    });
                }

                // Get all procurement plans
                var allPlans = await _context.ProcurementPlans.ToListAsync();

                if (!allPlans.Any())
                {
                    return Ok(new ApiResponse<List<ProcurementPlanSearchResultDto>>
                    {
                        Success = true,
                        Data = new List<ProcurementPlanSearchResultDto>(),
                        Message = "No procurement plans found in database"
                    });
                }

                // Calculate similarity scores
                var scoredPlans = allPlans.Select(plan =>
                {
                    double entityScore = _stringMatchingService.CalculateFuzzyMatchScore(
                        q, plan.ProcuringEntity ?? "");
                    double yearScore = _stringMatchingService.CalculateFuzzyMatchScore(
                        q, plan.Year ?? "");
                    double titleScore = _stringMatchingService.CalculateFuzzyMatchScore(
                        q, plan.Title ?? "");

                    // Weighted combination (prioritize entity name)
                    double combinedScore = (entityScore * 0.6) +
                                          (titleScore * 0.3) +
                                          (yearScore * 0.1);

                    return new ProcurementPlanSearchResultDto
                    {
                        Plan = MapToDto(plan),
                        MatchScore = combinedScore,
                        EntityScore = entityScore,
                        YearScore = yearScore,
                        TitleScore = titleScore,
                        IsExactMatch = plan.ProcuringEntity.Contains(q, StringComparison.OrdinalIgnoreCase)
                    };
                })
                .Where(x => x.MatchScore >= minScore)
                .OrderByDescending(x => x.MatchScore)
                .ThenByDescending(x => x.IsExactMatch)
                .Take(topN)
                .ToList();

                _logger.LogInformation(
                    $"Search for '{q}' returned {scoredPlans.Count} results with minimum score {minScore}");

                return Ok(new ApiResponse<List<ProcurementPlanSearchResultDto>>
                {
                    Success = true,
                    Message = $"Found {scoredPlans.Count} matches for '{q}'",
                    Data = scoredPlans,
                    TotalCount = scoredPlans.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching plans");
                return StatusCode(500, new ApiResponse<List<ProcurementPlanSearchResultDto>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // GET: api/procurementplansdb/search/advanced - ADVANCED FUZZY SEARCH
        [HttpGet("search/advanced")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<ProcurementPlanSearchResultDto>>>> AdvancedSearch(
            [FromQuery] string? entity,
            [FromQuery] string? year,
            [FromQuery] string? title,
            [FromQuery] int topN = 20,
            [FromQuery] double minScore = 0.3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(entity) &&
                    string.IsNullOrWhiteSpace(year) &&
                    string.IsNullOrWhiteSpace(title))
                {
                    return BadRequest(new ApiResponse<List<ProcurementPlanSearchResultDto>>
                    {
                        Success = false,
                        Message = "At least one search criterion must be provided"
                    });
                }

                var allPlans = await _context.ProcurementPlans.ToListAsync();

                var scoredPlans = allPlans.Select(plan =>
                {
                    double entityScore = string.IsNullOrWhiteSpace(entity) ? 0 :
                        _stringMatchingService.CalculateFuzzyMatchScore(entity, plan.ProcuringEntity ?? "");

                    double yearScore = string.IsNullOrWhiteSpace(year) ? 0 :
                        _stringMatchingService.CalculateFuzzyMatchScore(year, plan.Year ?? "");

                    double titleScore = string.IsNullOrWhiteSpace(title) ? 0 :
                        _stringMatchingService.CalculateFuzzyMatchScore(title, plan.Title ?? "");

                    // Calculate weights based on provided criteria
                    int criteriaCount = 0;
                    if (!string.IsNullOrWhiteSpace(entity)) criteriaCount++;
                    if (!string.IsNullOrWhiteSpace(year)) criteriaCount++;
                    if (!string.IsNullOrWhiteSpace(title)) criteriaCount++;

                    double combinedScore = (entityScore + yearScore + titleScore) / criteriaCount;

                    return new ProcurementPlanSearchResultDto
                    {
                        Plan = MapToDto(plan),
                        MatchScore = combinedScore,
                        EntityScore = entityScore,
                        YearScore = yearScore,
                        TitleScore = titleScore
                    };
                })
                .Where(x => x.MatchScore >= minScore)
                .OrderByDescending(x => x.MatchScore)
                .Take(topN)
                .ToList();

                return Ok(new ApiResponse<List<ProcurementPlanSearchResultDto>>
                {
                    Success = true,
                    Data = scoredPlans,
                    TotalCount = scoredPlans.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in advanced search");
                return StatusCode(500, new ApiResponse<List<ProcurementPlanSearchResultDto>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // GET: api/procurementplansdb/fuzzy-search - DETAILED FUZZY SEARCH WITH FULL DETAILS
        [HttpGet("fuzzy-search")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<ProcurementPlanDetailSearchResultDto>>>> FuzzySearch(
            [FromQuery] string q,
            [FromQuery] int maxResults = 5,
            [FromQuery] double minScore = 0.3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<List<ProcurementPlanDetailSearchResultDto>>
                    {
                        Success = false,
                        Message = "Search query is required"
                    });
                }

                _logger.LogInformation($"Fuzzy searching for procurement plans: '{q}'");

                // Get all plans with their items
                var allPlans = await _context.ProcurementPlans
                    .Include(p => p.Items)
                    .ToListAsync();

                if (!allPlans.Any())
                {
                    return Ok(new ApiResponse<List<ProcurementPlanDetailSearchResultDto>>
                    {
                        Success = true,
                        Message = "No procurement plans found",
                        Data = new List<ProcurementPlanDetailSearchResultDto>(),
                        TotalCount = 0
                    });
                }

                // Score and filter plans
                var scoredPlans = allPlans.Select(plan =>
                {
                    double entityScore = _stringMatchingService.CalculateFuzzyMatchScore(
                        q, plan.ProcuringEntity ?? "");
                    double yearScore = _stringMatchingService.CalculateFuzzyMatchScore(
                        q, plan.Year ?? "");
                    double titleScore = _stringMatchingService.CalculateFuzzyMatchScore(
                        q, plan.Title ?? "");

                    double combinedScore = (entityScore * 0.6) + (titleScore * 0.3) + (yearScore * 0.1);

                    return new ProcurementPlanDetailSearchResultDto
                    {
                        MatchInfo = new PlanMatchInfo
                        {
                            ProcuringEntity = plan.ProcuringEntity,
                            Year = plan.Year,
                            OverallScore = Math.Round(combinedScore, 3),
                            EntityScore = Math.Round(entityScore, 3),
                            YearScore = Math.Round(yearScore, 3),
                            TitleScore = Math.Round(titleScore, 3),
                            IsExactMatch = plan.ProcuringEntity.Contains(q, StringComparison.OrdinalIgnoreCase),
                            SourceUrl = plan.ViewAppUrl
                        },
                        ProcurementPlan = MapToDetailDto(plan)
                    };
                })
                .Where(x => x.MatchInfo.OverallScore >= minScore)
                .OrderByDescending(x => x.MatchInfo.OverallScore)
                .ThenByDescending(x => x.MatchInfo.IsExactMatch)
                .Take(maxResults)
                .ToList();

                return Ok(new ApiResponse<List<ProcurementPlanDetailSearchResultDto>>
                {
                    Success = true,
                    Message = $"Found {scoredPlans.Count} close matches for '{q}'",
                    Data = scoredPlans,
                    TotalCount = scoredPlans.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in fuzzy search: '{q}'");
                return StatusCode(500, new ApiResponse<List<ProcurementPlanDetailSearchResultDto>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // GET: api/procurementplansdb/entity/{entityName} - FUZZY ENTITY SEARCH
        [HttpGet("entity/{entityName}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<ProcurementPlanSearchResultDto>>>> GetByEntity(
            string entityName,
            [FromQuery] int topN = 50,
            [FromQuery] double minScore = 0.4)
        {
            try
            {
                var allPlans = await _context.ProcurementPlans.ToListAsync();

                var scoredPlans = allPlans.Select(plan =>
                {
                    double entityScore = _stringMatchingService.CalculateFuzzyMatchScore(
                        entityName,
                        plan.ProcuringEntity ?? "");

                    return new ProcurementPlanSearchResultDto
                    {
                        Plan = MapToDto(plan),
                        MatchScore = entityScore,
                        EntityScore = entityScore,
                        IsExactMatch = plan.ProcuringEntity.Contains(entityName, StringComparison.OrdinalIgnoreCase)
                    };
                })
                .Where(x => x.MatchScore >= minScore)
                .OrderByDescending(x => x.MatchScore)
                .ThenByDescending(x => x.IsExactMatch)
                .Take(topN)
                .ToList();

                return Ok(new ApiResponse<List<ProcurementPlanSearchResultDto>>
                {
                    Success = true,
                    Message = $"Found {scoredPlans.Count} plans matching '{entityName}'",
                    Data = scoredPlans,
                    TotalCount = scoredPlans.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting plans for entity {entityName}");
                return StatusCode(500, new ApiResponse<List<ProcurementPlanSearchResultDto>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // GET: api/procurementplansdb/year/{year}
        [HttpGet("year/{year}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<AnnualProcurementPlanDto>>>> GetByYear(string year)
        {
            try
            {
                var entities = await _context.ProcurementPlans
                    .Where(p => p.Year == year)
                    .OrderBy(p => p.ProcuringEntity)
                    .ToListAsync();

                return Ok(new ApiResponse<List<AnnualProcurementPlanDto>>
                {
                    Success = true,
                    Message = $"Found {entities.Count} plans for year {year}",
                    Data = entities.Select(MapToDto).ToList(),
                    TotalCount = entities.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting plans for year {year}");
                return StatusCode(500, new ApiResponse<List<AnnualProcurementPlanDto>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // GET: api/procurementplansdb/{id}/items
        [HttpGet("{id}/items")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<ProcurementPlanItem>>>> GetPlanItems(int id)
        {
            try
            {
                var plan = await _context.ProcurementPlans
                    .Include(p => p.Items)
                    .FirstOrDefaultAsync(p => p.Id == id);

                if (plan == null)
                    return NotFound(new ApiResponse<List<ProcurementPlanItem>>
                    {
                        Success = false,
                        Message = "Procurement plan not found"
                    });

                var items = plan.Items?.Select(MapItemToDto).ToList() ?? new List<ProcurementPlanItem>();

                return Ok(new ApiResponse<List<ProcurementPlanItem>>
                {
                    Success = true,
                    Data = items,
                    TotalCount = items.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting items for plan {id}");
                return StatusCode(500, new ApiResponse<List<ProcurementPlanItem>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // Helper mapping methods
        private AnnualProcurementPlanDto MapToDto(ProcurementPlanEntity e) => new AnnualProcurementPlanDto
        {
            Id = e.Id, // ✅ Include database ID
            ProcuringEntity = e.ProcuringEntity,
            Year = e.Year,
            ViewAppUrl = e.ViewAppUrl,
            TotalItems = e.TotalItems,
            TotalEstimatedValue = e.TotalEstimatedValue,
            LastScrapedAt = e.LastScrapedAt
        };

        private AnnualProcurementPlanDetailDto MapToDetailDto(ProcurementPlanEntity e) => new AnnualProcurementPlanDetailDto
        {
            Title = e.Title,
            ProcuringEntity = e.ProcuringEntity,
            Year = e.Year,
            SourceUrl = e.ViewAppUrl,
            TotalItems = e.TotalItems,
            TotalEstimatedValue = e.TotalEstimatedValue,
            ScrapedAt = e.LastScrapedAt,
            Items = e.Items?.Select(MapItemToDto).ToList() ?? new List<ProcurementPlanItem>()
        };

        private ProcurementPlanItem MapItemToDto(ProcurementPlanItemEntity e) => new ProcurementPlanItem
        {
            ItemId = e.ItemId,
            RefNo = e.RefNo,
            ClassOfProcurement = e.ClassOfProcurement,
            ObjectCode = e.ObjectCode,
            Description = e.Description,
            PmoEndUser = e.PmoEndUser,
            ProcurementMethod = e.ProcurementMethod,
            EoiPublicationDate = e.EoiPublicationDate,
            EoiClosingDate = e.EoiClosingDate,
            TenderPublicationDate = e.TenderPublicationDate,
            BidClosingDate = e.BidClosingDate,
            AwardNoticeDate = e.AwardNoticeDate,
            ContractSigningDate = e.ContractSigningDate,
            CycleDays = e.CycleDays,
            LeadTime = e.LeadTime,
            Spoc = e.Spoc,
            SourceOfFunds = e.SourceOfFunds,
            UnitOfMeasurement = e.UnitOfMeasurement,
            Quantity = e.Quantity,
            Comments = e.Comments,
            IsSupplement = e.IsSupplement,
            ParsedTenderPublicationDate = e.ParsedTenderPublicationDate,
            ParsedBidClosingDate = e.ParsedBidClosingDate,
            ParsedAwardNoticeDate = e.ParsedAwardNoticeDate,
            ParsedContractSigningDate = e.ParsedContractSigningDate
        };
    }

    // DTOs for search results
    public class ProcurementPlanSearchResultDto
    {
        public AnnualProcurementPlanDto Plan { get; set; }
        public double MatchScore { get; set; }
        public double EntityScore { get; set; }
        public double YearScore { get; set; }
        public double TitleScore { get; set; }
        public bool IsExactMatch { get; set; }
    }

    public class ProcurementPlanDetailSearchResultDto
    {
        public PlanMatchInfo MatchInfo { get; set; }
        public AnnualProcurementPlanDetailDto ProcurementPlan { get; set; }
    }

    public class PlanMatchInfo
    {
        public string ProcuringEntity { get; set; }
        public string Year { get; set; }
        public double OverallScore { get; set; }
        public double EntityScore { get; set; }
        public double YearScore { get; set; }
        public double TitleScore { get; set; }
        public bool IsExactMatch { get; set; }
        public string SourceUrl { get; set; }
    }
}