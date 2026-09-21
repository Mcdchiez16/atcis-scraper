using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(GroupName = "workflow")]
    [Authorize]
    public class TenderChecklistController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TenderChecklistController> _logger;

        public TenderChecklistController(
            ApplicationDbContext context,
            ILogger<TenderChecklistController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // POST: api/TenderChecklist
        [HttpPost]
        public async Task<ActionResult<TenderChecklistItemEntity>> CreateChecklistItem([FromBody] CreateChecklistItemRequest request)
        {
            try
            {
                // Verify tender exists
                var tenderExists = false;
                if (request.TenderType == "Live")
                {
                    tenderExists = await _context.LiveTenders.AnyAsync(t => t.Id == request.TenderId);
                }
                else if (request.TenderType == "Closed")
                {
                    tenderExists = await _context.ClosedTenders.AnyAsync(t => t.Id == request.TenderId);
                }

                if (!tenderExists)
                {
                    return NotFound($"{request.TenderType} tender with ID {request.TenderId} not found");
                }

                var checklistItem = new TenderChecklistItemEntity
                {
                    TenderId = request.TenderId,
                    TenderType = request.TenderType,
                    ItemTitle = request.ItemTitle,
                    ItemDescription = request.ItemDescription,
                    Status = "Pending",
                    IsRequired = request.IsRequired,
                    OrderIndex = request.OrderIndex ?? 0,
                    Category = request.Category,
                    DueDate = request.DueDate
                };

                _context.TenderChecklistItems.Add(checklistItem);
                await _context.SaveChangesAsync();

                return CreatedAtAction(nameof(GetChecklistItem), new { id = checklistItem.Id }, checklistItem);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating checklist item");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/TenderChecklist/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<TenderChecklistItemEntity>> GetChecklistItem(int id)
        {
            var item = await _context.TenderChecklistItems
                .Include(i => i.CompletedByUser)
                .FirstOrDefaultAsync(i => i.Id == id);

            if (item == null)
            {
                return NotFound();
            }

            return item;
        }

        // GET: api/TenderChecklist/tender/{tenderId}/{tenderType}
        [HttpGet("tender/{tenderId}/{tenderType}")]
        public async Task<ActionResult<IEnumerable<TenderChecklistItemEntity>>> GetTenderChecklist(
            int tenderId,
            string tenderType,
            [FromQuery] string? status = null,
            [FromQuery] string? category = null)
        {
            var query = _context.TenderChecklistItems
                .Include(i => i.CompletedByUser)
                .Where(i => i.TenderId == tenderId && i.TenderType == tenderType);

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(i => i.Status == status);
            }

            if (!string.IsNullOrEmpty(category))
            {
                query = query.Where(i => i.Category == category);
            }

            var items = await query
                .OrderBy(i => i.OrderIndex)
                .ThenBy(i => i.CreatedAt)
                .ToListAsync();

            return Ok(items);
        }

        // PUT: api/TenderChecklist/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateChecklistItem(int id, [FromBody] UpdateChecklistItemRequest request)
        {
            var item = await _context.TenderChecklistItems.FindAsync(id);

            if (item == null)
            {
                return NotFound();
            }

            item.ItemTitle = request.ItemTitle ?? item.ItemTitle;
            item.ItemDescription = request.ItemDescription;
            item.IsRequired = request.IsRequired ?? item.IsRequired;
            item.Category = request.Category ?? item.Category;
            item.DueDate = request.DueDate;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // PUT: api/TenderChecklist/{id}/status
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateChecklistItemStatus(int id, [FromBody] UpdateChecklistStatusRequest request)
        {
            var item = await _context.TenderChecklistItems.FindAsync(id);

            if (item == null)
            {
                return NotFound();
            }

            // Get current user
            var currentUsername = User.Identity?.Name;
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == currentUsername);

            if (user == null)
            {
                return Unauthorized();
            }

            item.Status = request.Status;

            if (request.Status == "Completed")
            {
                item.CompletedDate = DateTime.UtcNow;
                item.CompletedByUserId = user.Id;
                item.CompletionNotes = request.Notes;
            }
            else
            {
                // If changing from completed to another status, clear completion data
                if (item.Status == "Completed")
                {
                    item.CompletedDate = null;
                    item.CompletedByUserId = null;
                    item.CompletionNotes = null;
                }
            }

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // POST: api/TenderChecklist/bulk
        [HttpPost("bulk")]
        public async Task<ActionResult<IEnumerable<TenderChecklistItemEntity>>> CreateBulkChecklistItems(
            [FromBody] CreateBulkChecklistRequest request)
        {
            try
            {
                // Verify tender exists
                var tenderExists = false;
                if (request.TenderType == "Live")
                {
                    tenderExists = await _context.LiveTenders.AnyAsync(t => t.Id == request.TenderId);
                }
                else if (request.TenderType == "Closed")
                {
                    tenderExists = await _context.ClosedTenders.AnyAsync(t => t.Id == request.TenderId);
                }

                if (!tenderExists)
                {
                    return NotFound($"{request.TenderType} tender with ID {request.TenderId} not found");
                }

                var items = new List<TenderChecklistItemEntity>();
                int orderIndex = 0;

                foreach (var itemReq in request.Items)
                {
                    var item = new TenderChecklistItemEntity
                    {
                        TenderId = request.TenderId,
                        TenderType = request.TenderType,
                        ItemTitle = itemReq.ItemTitle,
                        ItemDescription = itemReq.ItemDescription,
                        Status = "Pending",
                        IsRequired = itemReq.IsRequired,
                        OrderIndex = itemReq.OrderIndex.HasValue ? itemReq.OrderIndex.Value : orderIndex++,
                        Category = itemReq.Category,
                        DueDate = itemReq.DueDate
                    };

                    items.Add(item);
                }

                _context.TenderChecklistItems.AddRange(items);
                await _context.SaveChangesAsync();

                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating bulk checklist items");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/TenderChecklist/templates
        [HttpGet("templates")]
        public ActionResult<IEnumerable<ChecklistTemplate>> GetChecklistTemplates()
        {
            var templates = new List<ChecklistTemplate>
            {
                new ChecklistTemplate
                {
                    Name = "Standard Tender Checklist",
                    Items = new[]
                    {
                        new ChecklistTemplateItem { Title = "PRAZ Certificate Uploaded", Category = "Documentation", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Tax Clearance Certificate", Category = "Documentation", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Company Registration Documents", Category = "Documentation", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Technical Proposal Prepared", Category = "Technical", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Financial Proposal Prepared", Category = "Financial", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Tender Reviewed by Legal", Category = "Compliance", IsRequired = false },
                        new ChecklistTemplateItem { Title = "Management Approval Obtained", Category = "Approval", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Final Submission Package Ready", Category = "Submission", IsRequired = true }
                    }
                },
                new ChecklistTemplate
                {
                    Name = "Construction Tender Checklist",
                    Items = new[]
                    {
                        new ChecklistTemplateItem { Title = "PRAZ Certificate", Category = "Documentation", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Tax Clearance", Category = "Documentation", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Engineering Qualifications", Category = "Technical", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Equipment List", Category = "Technical", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Previous Project References", Category = "Technical", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Bill of Quantities", Category = "Financial", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Safety Plan", Category = "Compliance", IsRequired = true },
                        new ChecklistTemplateItem { Title = "Environmental Clearance", Category = "Compliance", IsRequired = false }
                    }
                }
            };

            return Ok(templates);
        }

        // DELETE: api/TenderChecklist/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteChecklistItem(int id)
        {
            var item = await _context.TenderChecklistItems.FindAsync(id);

            if (item == null)
            {
                return NotFound();
            }

            _context.TenderChecklistItems.Remove(item); // Soft delete via SaveChanges
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    // DTOs
    public class CreateChecklistItemRequest
    {
        public int TenderId { get; set; }
        public string TenderType { get; set; } = "Live";
        public string ItemTitle { get; set; } = string.Empty;
        public string? ItemDescription { get; set; }
        public bool IsRequired { get; set; } = true;
        public int? OrderIndex { get; set; }
        public string? Category { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class UpdateChecklistItemRequest
    {
        public string? ItemTitle { get; set; }
        public string? ItemDescription { get; set; }
        public bool? IsRequired { get; set; }
        public string? Category { get; set; }
        public DateTime? DueDate { get; set; }
    }

    public class UpdateChecklistStatusRequest
    {
        public string Status { get; set; } = "Pending"; // Pending, InProgress, Completed, NotApplicable
        public string? Notes { get; set; }
    }

    public class CreateBulkChecklistRequest
    {
        public int TenderId { get; set; }
        public string TenderType { get; set; } = "Live";
        public List<CreateChecklistItemRequest> Items { get; set; } = new();
    }

    public class ChecklistTemplate
    {
        public string Name { get; set; } = string.Empty;
        public ChecklistTemplateItem[] Items { get; set; } = Array.Empty<ChecklistTemplateItem>();
    }

    public class ChecklistTemplateItem
    {
        public string Title { get; set; } = string.Empty;
        public string? Category { get; set; }
        public bool IsRequired { get; set; } = true;
    }
}
