using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Models;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "GuestAccess")]
    [ApiExplorerSettings(GroupName = "database")]
    public class ClosedTendersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ClosedTendersController> _logger;

        public ClosedTendersController(ApplicationDbContext context, ILogger<ClosedTendersController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<PaginatedResponse<Tender>>>> GetAll(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string sortBy = "ClosingDate",
            [FromQuery] bool descending = true)
        {
            try
            {
                var query = _context.ClosedTenders.AsQueryable();

                // Apply sorting
                query = sortBy.ToLower() switch
                {
                    "publishdate" => descending ? query.OrderByDescending(t => t.PublishDate) : query.OrderBy(t => t.PublishDate),
                    "closingdate" => descending ? query.OrderByDescending(t => t.ClosingDate) : query.OrderBy(t => t.ClosingDate),
                    "title" => descending ? query.OrderByDescending(t => t.Title) : query.OrderBy(t => t.Title),
                    "entity" => descending ? query.OrderByDescending(t => t.ProcuringEntity) : query.OrderBy(t => t.ProcuringEntity),
                    _ => descending ? query.OrderByDescending(t => t.ClosingDate) : query.OrderBy(t => t.ClosingDate)
                };

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var entities = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var tenders = entities.Select(e => MapToTender(e)).ToList();

                return Ok(new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = true,
                    Message = $"Retrieved {tenders.Count} closed tenders",
                    Data = new PaginatedResponse<Tender>
                    {
                        Items = tenders,
                        PageNumber = page,
                        PageSize = pageSize,
                        TotalCount = totalCount,
                        TotalPages = totalPages,
                        HasPreviousPage = page > 1,
                        HasNextPage = page < totalPages
                    },
                    TotalCount = totalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting closed tenders");
                return StatusCode(500, new ApiResponse<PaginatedResponse<Tender>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        [HttpGet("{id}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<Tender>>> GetById(int id)
        {
            try
            {
                var entity = await _context.ClosedTenders.FindAsync(id);

                if (entity == null)
                {
                    return NotFound(new ApiResponse<Tender>
                    {
                        Success = false,
                        Message = "Closed tender not found"
                    });
                }

                return Ok(new ApiResponse<Tender>
                {
                    Success = true,
                    Message = "Closed tender retrieved successfully",
                    Data = MapToTender(entity)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting closed tender {id}");
                return StatusCode(500, new ApiResponse<Tender>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        [HttpGet("tender/{tenderId}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<Tender>>> GetByTenderId(string tenderId)
        {
            try
            {
                var entity = await _context.ClosedTenders
                    .FirstOrDefaultAsync(t => t.TenderId == tenderId);

                if (entity == null)
                {
                    return NotFound(new ApiResponse<Tender>
                    {
                        Success = false,
                        Message = "Closed tender not found"
                    });
                }

                return Ok(new ApiResponse<Tender>
                {
                    Success = true,
                    Message = "Closed tender retrieved successfully",
                    Data = MapToTender(entity)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting closed tender {tenderId}");
                return StatusCode(500, new ApiResponse<Tender>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<Tender>>>> Search(
            [FromQuery] string q,
            [FromQuery] int maxResults = 50)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<List<Tender>>
                    {
                        Success = false,
                        Message = "Search query is required"
                    });
                }

                var entities = await _context.ClosedTenders
                    .Where(t => t.Title.Contains(q) ||
                               t.ProcuringEntity.Contains(q) ||
                               t.Scope.Contains(q) ||
                               t.ReferenceNumber.Contains(q))
                    .OrderByDescending(t => t.ClosingDate)
                    .Take(maxResults)
                    .ToListAsync();

                var tenders = entities.Select(MapToTender).ToList();

                return Ok(new ApiResponse<List<Tender>>
                {
                    Success = true,
                    Message = $"Found {tenders.Count} closed tenders matching '{q}'",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching closed tenders: {q}");
                return StatusCode(500, new ApiResponse<List<Tender>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        [HttpGet("entity/{entityName}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<Tender>>>> GetByEntity(
            string entityName,
            [FromQuery] int maxResults = 50)
        {
            try
            {
                var entities = await _context.ClosedTenders
                    .Where(t => t.ProcuringEntity.Contains(entityName))
                    .OrderByDescending(t => t.ClosingDate)
                    .Take(maxResults)
                    .ToListAsync();

                var tenders = entities.Select(MapToTender).ToList();

                return Ok(new ApiResponse<List<Tender>>
                {
                    Success = true,
                    Message = $"Found {tenders.Count} closed tenders from '{entityName}'",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting closed tenders for entity {entityName}");
                return StatusCode(500, new ApiResponse<List<Tender>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        [HttpGet("status/{status}")]
        [AllowAnonymous]
        public async Task<ActionResult<ApiResponse<List<Tender>>>> GetByStatus(
            string status,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20)
        {
            try
            {
                var query = _context.ClosedTenders
                    .Where(t => t.Status == status)
                    .OrderByDescending(t => t.ClosingDate);

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                var entities = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                var tenders = entities.Select(MapToTender).ToList();

                return Ok(new ApiResponse<List<Tender>>
                {
                    Success = true,
                    Message = $"Found {totalCount} closed tenders with status '{status}'",
                    Data = tenders,
                    TotalCount = totalCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting closed tenders by status {status}");
                return StatusCode(500, new ApiResponse<List<Tender>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // ✅ FIXED MAPPING METHOD - Now includes CategoryCodes and CategoryNames
        private Tender MapToTender(ClosedTenderEntity e)
        {
            return new Tender
            {
                Id = e.Id.ToString(),
                TenderId = e.TenderId,
                ReferenceNumber = e.ReferenceNumber,
                Title = e.Title,
                // ✅ Deserialize CategoryCodes from JSON
                CategoryCodes = string.IsNullOrEmpty(e.CategoryCodes)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(e.CategoryCodes) ?? new List<string>(),
                // ✅ Deserialize CategoryNames from JSON
                CategoryNames = string.IsNullOrEmpty(e.CategoryNames)
                    ? new List<string>()
                    : JsonSerializer.Deserialize<List<string>>(e.CategoryNames) ?? new List<string>(),
                ProcuringEntity = e.ProcuringEntity,
                Scope = e.Scope,
                PublishDate = e.PublishDate,
                ClosingDate = e.ClosingDate,
                DetailsUrl = e.DetailsUrl,
                SourceUrl = e.SourceUrl,
                PageNumber = e.PageNumber,
                ScrapedAt = e.LastScrapedAt
            };
        }
    }
}