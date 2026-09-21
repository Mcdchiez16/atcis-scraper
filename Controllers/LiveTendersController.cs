using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.Data.Repositories;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Models;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "GuestAccess")]
    [ApiExplorerSettings(GroupName = "database")]
    public class LiveTendersController : ControllerBase
    {
        private readonly ITenderRepository _tenderRepo;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LiveTendersController> _logger;
        private readonly IStringMatchingService _stringMatchingService;

        public LiveTendersController(
            ITenderRepository tenderRepo,
            ApplicationDbContext context,
            ILogger<LiveTendersController> logger,
            IStringMatchingService stringMatchingService)
        {
            _tenderRepo = tenderRepo;
            _context = context;
            _logger = logger;
            _stringMatchingService = stringMatchingService;
        }

        // GET: api/livetenders
        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<Tender>>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string sortBy = "PublishDate",
            [FromQuery] bool descending = true)
        {
            try
            {
                var query = _context.LiveTenders.AsQueryable();

                // Apply sorting
                query = sortBy.ToLower() switch
                {
                    "publishdate" => descending ? query.OrderByDescending(t => t.PublishDate) : query.OrderBy(t => t.PublishDate),
                    "closingdate" => descending ? query.OrderByDescending(t => t.ClosingDate) : query.OrderBy(t => t.ClosingDate),
                    "title" => descending ? query.OrderByDescending(t => t.Title) : query.OrderBy(t => t.Title),
                    "entity" => descending ? query.OrderByDescending(t => t.ProcuringEntity) : query.OrderBy(t => t.ProcuringEntity),
                    _ => descending ? query.OrderByDescending(t => t.PublishDate) : query.OrderBy(t => t.PublishDate)
                };

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var entities = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var tenders = entities.Select(MapToTender).ToList();

                var response = new PaginatedResponse<Tender>
                {
                    Items = tenders,
                    PageNumber = page,
                    PageSize = pageSize,
                    TotalCount = totalCount,
                    TotalPages = totalPages,
                    HasPreviousPage = page > 1,
                    HasNextPage = page < totalPages
                };

                return Ok(new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = true,
                    Message = $"Retrieved {tenders.Count} live tenders",
                    Data = response,
                    TotalCount = totalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting live tenders");
                return StatusCode(500, new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/livetenders/{id}
        [HttpGet("{id:int}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<Tender>>> GetById(int id)
        {
            try
            {
                var entity = await _tenderRepo.GetByIdAsync(id);

                if (entity == null)
                {
                    return NotFound(new ApiResponse<Tender>
                    {
                        Success = false,
                        Message = "Tender not found"
                    });
                }

                var tender = MapToTender(entity);

                return Ok(new ApiResponse<Tender>
                {
                    Success = true,
                    Message = "Tender retrieved successfully",
                    Data = tender
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting tender {id}");
                return StatusCode(500, new ApiResponse<Tender>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/livetenders/tender/{tenderId}
        [HttpGet("tender/{tenderId}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<Tender>>> GetByTenderId(string tenderId)
        {
            try
            {
                var entity = await _tenderRepo.GetByTenderIdAsync(tenderId);

                if (entity == null)
                {
                    return NotFound(new ApiResponse<Tender>
                    {
                        Success = false,
                        Message = "Tender not found"
                    });
                }

                var tender = MapToTender(entity);

                return Ok(new ApiResponse<Tender>
                {
                    Success = true,
                    Message = "Tender retrieved successfully",
                    Data = tender
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting tender {tenderId}");
                return StatusCode(500, new ApiResponse<Tender>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/livetenders/search - UPDATED WITH FUZZY MATCHING
        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<TenderSearchResultDto>>>> Search(
            [FromQuery] string q,
            [FromQuery] int topN = 20,
            [FromQuery] double minScore = 0.3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<List<TenderSearchResultDto>>
                    {
                        Success = false,
                        Message = "Search query is required"
                    });
                }

                // Get all live tenders
                var allTenders = await _context.LiveTenders.ToListAsync();

                if (!allTenders.Any())
                {
                    return Ok(new ApiResponse<List<TenderSearchResultDto>>
                    {
                        Success = true,
                        Data = new List<TenderSearchResultDto>(),
                        Message = "No tenders found in database"
                    });
                }

                // Calculate similarity scores
                var scoredTenders = allTenders.Select(tender =>
                {
                    // Calculate scores for different fields
                    double titleScore = _stringMatchingService.CalculateFuzzyMatchScore(q, tender.Title ?? "");
                    double entityScore = _stringMatchingService.CalculateFuzzyMatchScore(q, tender.ProcuringEntity ?? "");
                    double scopeScore = _stringMatchingService.CalculateFuzzyMatchScore(q, tender.Scope ?? "");
                    double refNumberScore = _stringMatchingService.CalculateFuzzyMatchScore(q, tender.ReferenceNumber ?? "");

                    // Check category names
                    double categoryScore = 0;
                    if (!string.IsNullOrEmpty(tender.CategoryNames))
                    {
                        try
                        {
                            var categories = JsonSerializer.Deserialize<List<string>>(tender.CategoryNames);
                            if (categories != null && categories.Any())
                            {
                                categoryScore = categories.Max(cat =>
                                    _stringMatchingService.CalculateFuzzyMatchScore(q, cat ?? ""));
                            }
                        }
                        catch { }
                    }

                    // Weighted combination (prioritize title and scope)
                    double combinedScore = (titleScore * 0.35) +
                                          (scopeScore * 0.25) +
                                          (entityScore * 0.20) +
                                          (categoryScore * 0.15) +
                                          (refNumberScore * 0.05);

                    return new TenderSearchResultDto
                    {
                        Tender = MapToTender(tender),
                        MatchScore = combinedScore,
                        TitleScore = titleScore,
                        ScopeScore = scopeScore,
                        EntityScore = entityScore,
                        CategoryScore = categoryScore,
                        RefNumberScore = refNumberScore
                    };
                })
                .Where(x => x.MatchScore >= minScore)
                .OrderByDescending(x => x.MatchScore)
                .Take(topN)
                .ToList();

                _logger.LogInformation(
                    $"Search for '{q}' returned {scoredTenders.Count} results with minimum score {minScore}");

                return Ok(new ApiResponse<List<TenderSearchResultDto>>
                {
                    Success = true,
                    Message = $"Found {scoredTenders.Count} tenders matching '{q}'",
                    Data = scoredTenders,
                    TotalCount = scoredTenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching tenders: {q}");
                return StatusCode(500, new ApiResponse<List<TenderSearchResultDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/livetenders/search/advanced - NEW ADVANCED SEARCH
        [HttpGet("search/advanced")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<TenderSearchResultDto>>>> AdvancedSearch(
            [FromQuery] string? title,
            [FromQuery] string? entity,
            [FromQuery] string? scope,
            [FromQuery] string? category,
            [FromQuery] int topN = 20,
            [FromQuery] double minScore = 0.3)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(title) &&
                    string.IsNullOrWhiteSpace(entity) &&
                    string.IsNullOrWhiteSpace(scope) &&
                    string.IsNullOrWhiteSpace(category))
                {
                    return BadRequest(new ApiResponse<List<TenderSearchResultDto>>
                    {
                        Success = false,
                        Message = "At least one search criterion must be provided"
                    });
                }

                var allTenders = await _context.LiveTenders.ToListAsync();

                var scoredTenders = allTenders.Select(tender =>
                {
                    double titleScore = string.IsNullOrWhiteSpace(title) ? 0 :
                        _stringMatchingService.CalculateFuzzyMatchScore(title, tender.Title ?? "");

                    double entityScore = string.IsNullOrWhiteSpace(entity) ? 0 :
                        _stringMatchingService.CalculateFuzzyMatchScore(entity, tender.ProcuringEntity ?? "");

                    double scopeScore = string.IsNullOrWhiteSpace(scope) ? 0 :
                        _stringMatchingService.CalculateFuzzyMatchScore(scope, tender.Scope ?? "");

                    double categoryScore = 0;
                    if (!string.IsNullOrWhiteSpace(category) && !string.IsNullOrEmpty(tender.CategoryNames))
                    {
                        try
                        {
                            var categories = JsonSerializer.Deserialize<List<string>>(tender.CategoryNames);
                            if (categories != null && categories.Any())
                            {
                                categoryScore = categories.Max(cat =>
                                    _stringMatchingService.CalculateFuzzyMatchScore(category, cat ?? ""));
                            }
                        }
                        catch { }
                    }

                    // Calculate weights based on which criteria were provided
                    int criteriaCount = 0;
                    if (!string.IsNullOrWhiteSpace(title)) criteriaCount++;
                    if (!string.IsNullOrWhiteSpace(entity)) criteriaCount++;
                    if (!string.IsNullOrWhiteSpace(scope)) criteriaCount++;
                    if (!string.IsNullOrWhiteSpace(category)) criteriaCount++;

                    double combinedScore = (titleScore + entityScore + scopeScore + categoryScore) / criteriaCount;

                    return new TenderSearchResultDto
                    {
                        Tender = MapToTender(tender),
                        MatchScore = combinedScore,
                        TitleScore = titleScore,
                        EntityScore = entityScore,
                        ScopeScore = scopeScore,
                        CategoryScore = categoryScore
                    };
                })
                .Where(x => x.MatchScore >= minScore)
                .OrderByDescending(x => x.MatchScore)
                .Take(topN)
                .ToList();

                return Ok(new ApiResponse<List<TenderSearchResultDto>>
                {
                    Success = true,
                    Data = scoredTenders,
                    TotalCount = scoredTenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in advanced search");
                return StatusCode(500, new ApiResponse<List<TenderSearchResultDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/livetenders/closing-soon
        [HttpGet("closing-soon")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<Tender>>>> GetClosingSoon([FromQuery] int days = 7)
        {
            try
            {
                var entities = await _tenderRepo.GetClosingSoonAsync(days);
                var tenders = entities.Select(MapToTender).ToList();

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

        // GET: api/livetenders/entity/{entityName} - UPDATED WITH FUZZY MATCHING
        [HttpGet("entity/{entityName}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<TenderSearchResultDto>>>> GetByEntity(
            string entityName,
            [FromQuery] int topN = 50,
            [FromQuery] double minScore = 0.5)
        {
            try
            {
                var allTenders = await _context.LiveTenders.ToListAsync();

                var scoredTenders = allTenders.Select(tender =>
                {
                    double entityScore = _stringMatchingService.CalculateFuzzyMatchScore(
                        entityName,
                        tender.ProcuringEntity ?? "");

                    return new TenderSearchResultDto
                    {
                        Tender = MapToTender(tender),
                        MatchScore = entityScore,
                        EntityScore = entityScore
                    };
                })
                .Where(x => x.MatchScore >= minScore)
                .OrderByDescending(x => x.MatchScore)
                .Take(topN)
                .ToList();

                return Ok(new ApiResponse<List<TenderSearchResultDto>>
                {
                    Success = true,
                    Message = $"Found {scoredTenders.Count} tenders from entities matching '{entityName}'",
                    Data = scoredTenders,
                    TotalCount = scoredTenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting tenders for entity {entityName}");
                return StatusCode(500, new ApiResponse<List<TenderSearchResultDto>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/livetenders/{id}/audit-logs
        [HttpGet("{id}/audit-logs")]
        [Authorize(Policy = "UserOrAdmin")]
        public async Task<ActionResult<ApiResponse<List<TenderAuditLog>>>> GetAuditLogs(int id)
        {
            try
            {
                var entity = await _context.LiveTenders
                    .Include(t => t.AuditLogs)
                    .FirstOrDefaultAsync(t => t.Id == id);

                if (entity == null)
                {
                    return NotFound(new ApiResponse<List<TenderAuditLog>>
                    {
                        Success = false,
                        Message = "Tender not found"
                    });
                }

                var auditLogs = entity.AuditLogs.OrderByDescending(a => a.ChangeDate).ToList();

                return Ok(new ApiResponse<List<TenderAuditLog>>
                {
                    Success = true,
                    Message = "Audit logs retrieved successfully",
                    Data = auditLogs,
                    TotalCount = auditLogs.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting audit logs for tender {id}");
                return StatusCode(500, new ApiResponse<List<TenderAuditLog>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // POST: api/livetenders
        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ApiResponse<Tender>>> Create([FromBody] Tender tender)
        {
            try
            {
                var username = User.Identity?.Name ?? "System";
                var entity = MapToEntity(tender);

                var created = await _tenderRepo.UpsertAsync(entity, username);
                var result = MapToTender(created);

                return CreatedAtAction(nameof(GetById), new { id = created.Id }, new ApiResponse<Tender>
                {
                    Success = true,
                    Message = "Tender created successfully",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating tender");
                return StatusCode(500, new ApiResponse<Tender>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // PUT: api/livetenders/{id}
        [HttpPut("{id}")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ApiResponse<Tender>>> Update(int id, [FromBody] Tender tender)
        {
            try
            {
                var existing = await _tenderRepo.GetByIdAsync(id);
                if (existing == null)
                {
                    return NotFound(new ApiResponse<Tender>
                    {
                        Success = false,
                        Message = "Tender not found"
                    });
                }

                var username = User.Identity?.Name ?? "System";
                existing.Title = tender.Title;
                existing.ReferenceNumber = tender.ReferenceNumber;
                existing.ProcuringEntity = tender.ProcuringEntity;
                existing.Scope = tender.Scope;
                existing.PublishDate = tender.PublishDate;
                existing.ClosingDate = tender.ClosingDate;
                existing.DetailsUrl = tender.DetailsUrl;
                existing.CategoryCodes = JsonSerializer.Serialize(tender.CategoryCodes ?? new List<string>());
                existing.CategoryNames = JsonSerializer.Serialize(tender.CategoryNames ?? new List<string>());

                var updated = await _tenderRepo.UpdateAsync(existing);
                var result = MapToTender(updated);

                return Ok(new ApiResponse<Tender>
                {
                    Success = true,
                    Message = "Tender updated successfully",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating tender {id}");
                return StatusCode(500, new ApiResponse<Tender>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // DELETE: api/livetenders/{id}
        [HttpDelete("{id}")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ApiResponse<bool>>> Delete(int id)
        {
            try
            {
                var result = await _tenderRepo.DeleteAsync(id, hardDelete: false);

                if (!result)
                {
                    return NotFound(new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Tender not found"
                    });
                }

                return Ok(new ApiResponse<bool>
                {
                    Success = true,
                    Message = "Tender deleted successfully",
                    Data = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting tender {id}");
                return StatusCode(500, new ApiResponse<bool>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // POST: api/livetenders/{id}/restore
        [HttpPost("{id}/restore")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ApiResponse<bool>>> Restore(int id)
        {
            try
            {
                var result = await _tenderRepo.RestoreAsync(id);

                if (!result)
                {
                    return NotFound(new ApiResponse<bool>
                    {
                        Success = false,
                        Message = "Tender not found or not deleted"
                    });
                }

                return Ok(new ApiResponse<bool>
                {
                    Success = true,
                    Message = "Tender restored successfully",
                    Data = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error restoring tender {id}");
                return StatusCode(500, new ApiResponse<bool>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // Helper methods
        private Tender MapToTender(LiveTenderEntity entity)
        {
            return new Tender
            {
                Id = entity.Id.ToString(),
                TenderId = entity.TenderId,
                ReferenceNumber = entity.ReferenceNumber,
                Title = entity.Title,
                CategoryCodes = string.IsNullOrEmpty(entity.CategoryCodes)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(entity.CategoryCodes) ?? new List<string>(),
                CategoryNames = string.IsNullOrEmpty(entity.CategoryNames)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(entity.CategoryNames) ?? new List<string>(),
                ProcuringEntity = entity.ProcuringEntity,
                Scope = entity.Scope,
                PublishDate = entity.PublishDate,
                ClosingDate = entity.ClosingDate,
                DetailsUrl = entity.DetailsUrl,
                SourceUrl = entity.SourceUrl,
                PageNumber = entity.PageNumber,
                ScrapedAt = entity.LastScrapedAt
            };
        }

        private LiveTenderEntity MapToEntity(Tender tender)
        {
            return new LiveTenderEntity
            {
                TenderId = tender.TenderId,
                ReferenceNumber = tender.ReferenceNumber,
                Title = tender.Title,
                CategoryCodes = JsonSerializer.Serialize(tender.CategoryCodes ?? new List<string>()),
                CategoryNames = JsonSerializer.Serialize(tender.CategoryNames ?? new List<string>()),
                ProcuringEntity = tender.ProcuringEntity,
                Scope = tender.Scope,
                PublishDate = tender.PublishDate,
                ClosingDate = tender.ClosingDate,
                DetailsUrl = tender.DetailsUrl,
                SourceUrl = tender.SourceUrl,
                PageNumber = tender.PageNumber,
                LastScrapedAt = DateTime.UtcNow
            };
        }
    }

    // DTO for tender search results with scoring
    public class TenderSearchResultDto
    {
        public Tender? Tender { get; set; }
        public double MatchScore { get; set; }
        public double TitleScore { get; set; }
        public double ScopeScore { get; set; }
        public double EntityScore { get; set; }
        public double CategoryScore { get; set; }
        public double RefNumberScore { get; set; }
    }
}