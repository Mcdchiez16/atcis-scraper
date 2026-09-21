using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    // ==================== AWARDS CONTROLLER ====================
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "GuestAccess")]
    [ApiExplorerSettings(GroupName = "database")]
    public class AwardsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AwardsController> _logger;
        private readonly IStringMatchingService _stringMatchingService;

        public AwardsController(
            ApplicationDbContext context,
            ILogger<AwardsController> logger,
            IStringMatchingService stringMatchingService)
        {
            _context = context;
            _logger = logger;
            _stringMatchingService = stringMatchingService;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<AwardNoticeDto>>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var query = _context.AwardNotices.OrderByDescending(a => a.ParsedAwardDate);
                var totalCount = await query.CountAsync();
                var entities = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
                var awards = entities.Select(MapToDto).ToList();

                return Ok(new ApiResponse<PaginatedResponse<AwardNoticeDto>>
                {
                    Success = true,
                    Data = new PaginatedResponse<AwardNoticeDto>
                    {
                        Items = awards,
                        PageNumber = page,
                        PageSize = pageSize,
                        TotalCount = totalCount,
                        TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize)
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting awards");
                return StatusCode(500, new ApiResponse<PaginatedResponse<AwardNoticeDto>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // GET by database ID
        [HttpGet("{id:int}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<AwardNoticeDto>>> GetById(int id)
        {
            var entity = await _context.AwardNotices.FindAsync(id);
            if (entity == null)
                return NotFound(new ApiResponse<AwardNoticeDto>
                {
                    Success = false,
                    Message = "Award not found"
                });

            return Ok(new ApiResponse<AwardNoticeDto>
            {
                Success = true,
                Data = MapToDto(entity)
            });
        }

        // GET by TenderId (string)
        [HttpGet("tender/{tenderId}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<AwardNoticeDto>>>> GetByTenderId(string tenderId)
        {
            try
            {
                var entities = await _context.AwardNotices
                    .Where(a => a.TenderId == tenderId)
                    .OrderByDescending(a => a.ParsedAwardDate)
                    .ToListAsync();

                if (!entities.Any())
                    return NotFound(new ApiResponse<List<AwardNoticeDto>>
                    {
                        Success = false,
                        Message = $"No awards found for Tender ID: {tenderId}"
                    });

                return Ok(new ApiResponse<List<AwardNoticeDto>>
                {
                    Success = true,
                    Data = entities.Select(MapToDto).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting awards by tender ID");
                return StatusCode(500, new ApiResponse<List<AwardNoticeDto>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // Search with fuzzy matching using string similarity algorithms
        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<AwardSearchResultDto>>>> Search(
            [FromQuery] string q,
            [FromQuery] int topN = 10,
            [FromQuery] double minScore = 0.3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<List<AwardSearchResultDto>>
                    {
                        Success = false,
                        Message = "Search query cannot be empty"
                    });
                }

                // Get all awards
                var allAwards = await _context.AwardNotices.ToListAsync();

                if (!allAwards.Any())
                {
                    return Ok(new ApiResponse<List<AwardSearchResultDto>>
                    {
                        Success = true,
                        Data = new List<AwardSearchResultDto>(),
                        Message = "No awards found in database"
                    });
                }

                // Calculate similarity scores for each award
                var scoredAwards = allAwards.Select(award =>
                {
                    // Calculate scores for different fields
                    double titleScore = _stringMatchingService.CalculateFuzzyMatchScore(q, award.AwardTitle ?? "");
                    double awardeeScore = _stringMatchingService.CalculateFuzzyMatchScore(q, award.Awardee ?? "");
                    double tenderIdScore = _stringMatchingService.CalculateFuzzyMatchScore(q, award.TenderId ?? "");
                    double awardNoticeNumberScore = _stringMatchingService.CalculateFuzzyMatchScore(q, award.AwardNoticeNumber ?? "");

                    // Weighted combination (prioritize title and awardee)
                    double combinedScore = (titleScore * 0.4) +
                                          (awardeeScore * 0.3) +
                                          (tenderIdScore * 0.2) +
                                          (awardNoticeNumberScore * 0.1);

                    return new AwardSearchResultDto
                    {
                        Award = MapToDto(award),
                        MatchScore = combinedScore,
                        TitleScore = titleScore,
                        AwardeeScore = awardeeScore,
                        TenderIdScore = tenderIdScore,
                        AwardNoticeNumberScore = awardNoticeNumberScore
                    };
                })
                .Where(x => x.MatchScore >= minScore)
                .OrderByDescending(x => x.MatchScore)
                .Take(topN)
                .ToList();

                _logger.LogInformation(
                    $"Search for '{q}' returned {scoredAwards.Count} results with minimum score {minScore}");

                return Ok(new ApiResponse<List<AwardSearchResultDto>>
                {
                    Success = true,
                    Data = scoredAwards,
                    Message = $"Found {scoredAwards.Count} matches for '{q}'"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching awards");
                return StatusCode(500, new ApiResponse<List<AwardSearchResultDto>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        // Advanced search with multiple criteria
        [HttpGet("search/advanced")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<AwardSearchResultDto>>>> AdvancedSearch(
            [FromQuery] string? title,
            [FromQuery] string? awardee,
            [FromQuery] string? tenderId,
            [FromQuery] int topN = 10,
            [FromQuery] double minScore = 0.3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(title) &&
                    string.IsNullOrWhiteSpace(awardee) &&
                    string.IsNullOrWhiteSpace(tenderId))
                {
                    return BadRequest(new ApiResponse<List<AwardSearchResultDto>>
                    {
                        Success = false,
                        Message = "At least one search criterion must be provided"
                    });
                }

                var allAwards = await _context.AwardNotices.ToListAsync();

                var scoredAwards = allAwards.Select(award =>
                {
                    double titleScore = string.IsNullOrWhiteSpace(title) ? 0 :
                        _stringMatchingService.CalculateFuzzyMatchScore(title, award.AwardTitle ?? "");

                    double awardeeScore = string.IsNullOrWhiteSpace(awardee) ? 0 :
                        _stringMatchingService.CalculateFuzzyMatchScore(awardee, award.Awardee ?? "");

                    double tenderIdScore = string.IsNullOrWhiteSpace(tenderId) ? 0 :
                        _stringMatchingService.CalculateFuzzyMatchScore(tenderId, award.TenderId ?? "");

                    // Calculate weights based on which criteria were provided
                    int criteriaCount = 0;
                    if (!string.IsNullOrWhiteSpace(title)) criteriaCount++;
                    if (!string.IsNullOrWhiteSpace(awardee)) criteriaCount++;
                    if (!string.IsNullOrWhiteSpace(tenderId)) criteriaCount++;

                    double combinedScore = (titleScore + awardeeScore + tenderIdScore) / criteriaCount;

                    return new AwardSearchResultDto
                    {
                        Award = MapToDto(award),
                        MatchScore = combinedScore,
                        TitleScore = titleScore,
                        AwardeeScore = awardeeScore,
                        TenderIdScore = tenderIdScore
                    };
                })
                .Where(x => x.MatchScore >= minScore)
                .OrderByDescending(x => x.MatchScore)
                .Take(topN)
                .ToList();

                return Ok(new ApiResponse<List<AwardSearchResultDto>>
                {
                    Success = true,
                    Data = scoredAwards
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in advanced search");
                return StatusCode(500, new ApiResponse<List<AwardSearchResultDto>>
                {
                    Success = false,
                    Message = ex.Message
                });
            }
        }

        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ApiResponse<AwardNoticeDto>>> Create([FromBody] AwardNoticeDto dto)
        {
            try
            {
                var entity = new AwardNoticeEntity
                {
                    AwardNoticeNumber = dto.AwardNoticeNumber,
                    TenderId = dto.TenderId,
                    AwardTitle = dto.AwardTitle,
                    Awardee = dto.Awardee,
                    AwardDate = dto.AwardDate,
                    DetailsUrl = dto.DetailsUrl,
                    Currency = dto.Currency ?? "USD",
                    ContractValue = dto.ContractValue
                };

                await _context.AwardNotices.AddAsync(entity);
                await _context.SaveChangesAsync();

                return Ok(new ApiResponse<AwardNoticeDto>
                {
                    Success = true,
                    Data = MapToDto(entity),
                    Message = "Award created successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating award");
                return StatusCode(500, new ApiResponse<AwardNoticeDto>
                {
                    Success = false,
                    Message = $"Error creating award: {ex.Message}"
                });
            }
        }

        [HttpPut("{id}")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ApiResponse<AwardNoticeDto>>> Update(int id, [FromBody] AwardNoticeDto dto)
        {
            try
            {
                var entity = await _context.AwardNotices.FindAsync(id);
                if (entity == null)
                    return NotFound(new ApiResponse<AwardNoticeDto>
                    {
                        Success = false,
                        Message = "Award not found"
                    });

                entity.AwardNoticeNumber = dto.AwardNoticeNumber;
                entity.TenderId = dto.TenderId;
                entity.AwardTitle = dto.AwardTitle;
                entity.Awardee = dto.Awardee;
                entity.AwardDate = dto.AwardDate;
                entity.DetailsUrl = dto.DetailsUrl;
                entity.Currency = dto.Currency ?? "USD";
                entity.ContractValue = dto.ContractValue;

                await _context.SaveChangesAsync();

                return Ok(new ApiResponse<AwardNoticeDto>
                {
                    Success = true,
                    Data = MapToDto(entity),
                    Message = "Award updated successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating award");
                return StatusCode(500, new ApiResponse<AwardNoticeDto>
                {
                    Success = false,
                    Message = $"Error updating award: {ex.Message}"
                });
            }
        }

        [HttpDelete("{id}")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(int id)
        {
            try
            {
                var entity = await _context.AwardNotices.FindAsync(id);
                if (entity == null)
                    return NotFound(new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Award not found"
                    });

                _context.AwardNotices.Remove(entity);
                await _context.SaveChangesAsync();

                return Ok(new ApiResponse<bool>
                {
                    Success = true,
                    Data = true,
                    Message = "Award deleted successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting award");
                return StatusCode(500, new ApiResponse<bool>
                {
                    Success = false,
                    Message = $"Error deleting award: {ex.Message}"
                });
            }
        }

        private AwardNoticeDto MapToDto(AwardNoticeEntity e) => new AwardNoticeDto
        {
            Id = e.Id,
            AwardNoticeNumber = e.AwardNoticeNumber,
            TenderId = e.TenderId,
            AwardTitle = e.AwardTitle,
            Awardee = e.Awardee,
            AwardDate = e.AwardDate,
            DetailsUrl = e.DetailsUrl,
            Currency = e.Currency,
            ContractValue = e.ContractValue,
            ParsedAwardDate = e.ParsedAwardDate
        };
    }

    // DTO for search results with scoring
    public class AwardSearchResultDto
    {
        public AwardNoticeDto Award { get; set; }
        public double MatchScore { get; set; }
        public double TitleScore { get; set; }
        public double AwardeeScore { get; set; }
        public double TenderIdScore { get; set; }
        public double AwardNoticeNumberScore { get; set; }
    }
}