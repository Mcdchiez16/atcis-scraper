using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(GroupName = "workflow")]
    [Authorize]
    public class TenderAssignmentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<TenderAssignmentsController> _logger;

        public TenderAssignmentsController(
            ApplicationDbContext context,
            IEmailService emailService,
            ILogger<TenderAssignmentsController> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        // POST: api/TenderAssignments/assign
        [HttpPost("assign")]
        public async Task<ActionResult<TenderAssignmentEntity>> AssignTender([FromBody] AssignTenderRequest request)
        {
            try
            {
                // Get current user
                var currentUsername = User.Identity?.Name;
                var assignedByUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == currentUsername);

                if (assignedByUser == null)
                {
                    return Unauthorized("User not found");
                }

                // Get assigned to user
                var assignedToUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == request.AssignedToUserId);

                if (assignedToUser == null)
                {
                    return NotFound($"User with ID {request.AssignedToUserId} not found");
                }

                // Verify tender exists
                var tenderExists = false;
                string tenderTitle = "";
                string tenderReference = "";
                string tenderUrl = "";

                if (request.TenderType == "Live")
                {
                    var tender = await _context.LiveTenders
                        .FirstOrDefaultAsync(t => t.Id == request.TenderId);
                    if (tender != null)
                    {
                        tenderExists = true;
                        tenderTitle = tender.Title;
                        tenderReference = tender.ReferenceNumber;
                        tenderUrl = tender.DetailsUrl;
                    }
                }
                else if (request.TenderType == "Closed")
                {
                    var tender = await _context.ClosedTenders
                        .FirstOrDefaultAsync(t => t.Id == request.TenderId);
                    if (tender != null)
                    {
                        tenderExists = true;
                        tenderTitle = tender.Title;
                        tenderReference = tender.ReferenceNumber;
                        tenderUrl = tender.DetailsUrl;
                    }
                }

                if (!tenderExists)
                {
                    return NotFound($"{request.TenderType} tender with ID {request.TenderId} not found");
                }

                // Create assignment
                var assignment = new TenderAssignmentEntity
                {
                    TenderId = request.TenderId,
                    TenderType = request.TenderType,
                    AssignedToUserId = request.AssignedToUserId,
                    AssignedByUserId = assignedByUser.Id,
                    AssignedDate = DateTime.UtcNow,
                    DueDate = request.DueDate,
                    Status = "Pending",
                    Notes = request.Notes,
                    AssignmentInstructions = request.AssignmentInstructions
                };

                _context.TenderAssignments.Add(assignment);
                await _context.SaveChangesAsync();

                // Send email notification
                try
                {
                    var emailSent = await _emailService.SendTenderAssignmentEmailAsync(
                        assignedToUser.Email,
                        $"{assignedToUser.FirstName} {assignedToUser.LastName}".Trim(),
                        tenderTitle,
                        tenderReference,
                        tenderUrl,
                        $"{assignedByUser.FirstName} {assignedByUser.LastName}".Trim(),
                        request.AssignmentInstructions ?? "",
                        request.DueDate
                    );

                    assignment.EmailSent = emailSent;
                    assignment.EmailSentAt = emailSent ? DateTime.UtcNow : null;
                    await _context.SaveChangesAsync();

                    _logger.LogInformation(
                        "Tender assignment created. Tender: {TenderReference}, Assigned to: {AssignedTo}, Email sent: {EmailSent}",
                        tenderReference, assignedToUser.Email, emailSent);
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx, "Failed to send assignment email");
                    // Continue even if email fails
                }

                // Load navigation properties for response
                await _context.Entry(assignment).Reference(a => a.AssignedToUser).LoadAsync();
                await _context.Entry(assignment).Reference(a => a.AssignedByUser).LoadAsync();

                return CreatedAtAction(nameof(GetAssignment), new { id = assignment.Id }, assignment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning tender");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/TenderAssignments/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<TenderAssignmentEntity>> GetAssignment(int id)
        {
            var assignment = await _context.TenderAssignments
                .Include(a => a.AssignedToUser)
                .Include(a => a.AssignedByUser)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (assignment == null)
            {
                return NotFound();
            }

            return assignment;
        }

        // GET: api/TenderAssignments/my-assignments
        [HttpGet("my-assignments")]
        public async Task<ActionResult<IEnumerable<TenderAssignmentEntity>>> GetMyAssignments(
            [FromQuery] string? status = null)
        {
            var currentUsername = User.Identity?.Name;
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == currentUsername);

            if (user == null)
            {
                return Unauthorized();
            }

            var query = _context.TenderAssignments
                .Include(a => a.AssignedByUser)
                .Where(a => a.AssignedToUserId == user.Id);

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(a => a.Status == status);
            }

            var assignments = await query
                .OrderByDescending(a => a.AssignedDate)
                .ToListAsync();

            return Ok(assignments);
        }

        // GET: api/TenderAssignments/tender/{tenderId}/{tenderType}
        [HttpGet("tender/{tenderId}/{tenderType}")]
        public async Task<ActionResult<IEnumerable<TenderAssignmentEntity>>> GetTenderAssignments(
            int tenderId,
            string tenderType)
        {
            var assignments = await _context.TenderAssignments
                .Include(a => a.AssignedToUser)
                .Include(a => a.AssignedByUser)
                .Where(a => a.TenderId == tenderId && a.TenderType == tenderType)
                .OrderByDescending(a => a.AssignedDate)
                .ToListAsync();

            return Ok(assignments);
        }

        // PUT: api/TenderAssignments/{id}/status
        [HttpPut("{id}/status")]
        public async Task<IActionResult> UpdateAssignmentStatus(int id, [FromBody] UpdateAssignmentStatusRequest request)
        {
            var assignment = await _context.TenderAssignments.FindAsync(id);

            if (assignment == null)
            {
                return NotFound();
            }

            // Verify user is assigned to this tender
            var currentUsername = User.Identity?.Name;
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == currentUsername);

            if (user == null || assignment.AssignedToUserId != user.Id)
            {
                return Forbid();
            }

            assignment.Status = request.Status;
            
            if (request.Status == "Completed")
            {
                assignment.CompletedAt = DateTime.UtcNow;
                assignment.CompletionNotes = request.Notes;
            }

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/TenderAssignments/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAssignment(int id)
        {
            var assignment = await _context.TenderAssignments.FindAsync(id);

            if (assignment == null)
            {
                return NotFound();
            }

            // Verify user is the one who created the assignment
            var currentUsername = User.Identity?.Name;
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == currentUsername);

            if (user == null || assignment.AssignedByUserId != user.Id)
            {
                return Forbid();
            }

            _context.TenderAssignments.Remove(assignment); // Soft delete via SaveChanges
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    // DTOs
    public class AssignTenderRequest
    {
        public int TenderId { get; set; }
        public string TenderType { get; set; } = "Live"; // Live or Closed
        public int AssignedToUserId { get; set; }
        public DateTime? DueDate { get; set; }
        public string? Notes { get; set; }
        public string? AssignmentInstructions { get; set; }
    }

    public class UpdateAssignmentStatusRequest
    {
        public string Status { get; set; } = "InProgress"; // Pending, InProgress, Completed, Cancelled
        public string? Notes { get; set; }
    }
}
