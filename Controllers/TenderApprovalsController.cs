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
    public class TenderApprovalsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<TenderApprovalsController> _logger;

        public TenderApprovalsController(
            ApplicationDbContext context,
            IEmailService emailService,
            ILogger<TenderApprovalsController> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        // POST: api/TenderApprovals/request
        [HttpPost("request")]
        public async Task<ActionResult<TenderApprovalEntity>> RequestApproval([FromBody] RequestApprovalRequest request)
        {
            try
            {
                // Get current user (requester)
                var currentUsername = User.Identity?.Name;
                var requestedByUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == currentUsername);

                if (requestedByUser == null)
                {
                    return Unauthorized("User not found");
                }

                // Get approver user
                var approverUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == request.ApproverUserId);

                if (approverUser == null)
                {
                    return NotFound($"Approver user with ID {request.ApproverUserId} not found");
                }

                // Verify tender exists
                var tenderExists = false;
                string tenderTitle = "";
                string tenderReference = "";

                if (request.TenderType == "Live")
                {
                    var tender = await _context.LiveTenders
                        .FirstOrDefaultAsync(t => t.Id == request.TenderId);
                    if (tender != null)
                    {
                        tenderExists = true;
                        tenderTitle = tender.Title;
                        tenderReference = tender.ReferenceNumber;
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
                    }
                }

                if (!tenderExists)
                {
                    return NotFound($"{request.TenderType} tender with ID {request.TenderId} not found");
                }

                // Check if there's a previous approval in the chain
                int? previousApprovalId = null;
                if (request.OrderIndex > 0)
                {
                    var previousApproval = await _context.TenderApprovals
                        .Where(a => a.TenderId == request.TenderId &&
                                   a.TenderType == request.TenderType &&
                                   a.OrderIndex == request.OrderIndex - 1)
                        .FirstOrDefaultAsync();

                    if (previousApproval != null)
                    {
                        if (previousApproval.ApprovalStatus != "Approved")
                        {
                            return BadRequest("Previous approval in the chain must be approved first");
                        }
                        previousApprovalId = previousApproval.Id;
                    }
                }

                // Create approval request
                var approval = new TenderApprovalEntity
                {
                    TenderId = request.TenderId,
                    TenderType = request.TenderType,
                    ApprovalStage = request.ApprovalStage,
                    ApproverUserId = request.ApproverUserId,
                    ApprovalStatus = "Pending",
                    RequestedDate = DateTime.UtcNow,
                    RequestDetails = request.RequestDetails,
                    RequestedByUserId = requestedByUser.Id,
                    OrderIndex = request.OrderIndex,
                    IsRequired = request.IsRequired,
                    PreviousApprovalId = previousApprovalId
                };

                _context.TenderApprovals.Add(approval);
                await _context.SaveChangesAsync();

                // Send email notification to approver
                try
                {
                    await _emailService.SendApprovalRequestEmailAsync(
                        approverUser.Email,
                        $"{approverUser.FirstName} {approverUser.LastName}".Trim(),
                        tenderTitle,
                        tenderReference,
                        request.ApprovalStage,
                        $"{requestedByUser.FirstName} {requestedByUser.LastName}".Trim()
                    );

                    _logger.LogInformation(
                        "Approval request created. Tender: {TenderReference}, Stage: {Stage}, Approver: {Approver}",
                        tenderReference, request.ApprovalStage, approverUser.Email);
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx, "Failed to send approval request email");
                }

                // Load navigation properties
                await _context.Entry(approval).Reference(a => a.ApproverUser).LoadAsync();
                await _context.Entry(approval).Reference(a => a.RequestedByUser).LoadAsync();

                return CreatedAtAction(nameof(GetApproval), new { id = approval.Id }, approval);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requesting approval");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // PUT: api/TenderApprovals/{id}/respond
        [HttpPut("{id}/respond")]
        public async Task<IActionResult> RespondToApproval(int id, [FromBody] ApprovalResponseRequest request)
        {
            try
            {
                var approval = await _context.TenderApprovals
                    .Include(a => a.ApproverUser)
                    .Include(a => a.RequestedByUser)
                    .FirstOrDefaultAsync(a => a.Id == id);

                if (approval == null)
                {
                    return NotFound();
                }

                // Verify current user is the approver
                var currentUsername = User.Identity?.Name;
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == currentUsername);

                if (user == null || approval.ApproverUserId != user.Id)
                {
                    return Forbid("You are not authorized to respond to this approval request");
                }

                if (approval.ApprovalStatus != "Pending")
                {
                    return BadRequest("This approval has already been processed");
                }

                // Update approval
                approval.ApprovalStatus = request.Status;
                approval.ReviewedDate = DateTime.UtcNow;
                approval.ApproverComments = request.Comments;

                await _context.SaveChangesAsync();

                // Get tender details for email
                string tenderTitle = "";
                string tenderReference = "";

                if (approval.TenderType == "Live")
                {
                    var tender = await _context.LiveTenders.FindAsync(approval.TenderId);
                    if (tender != null)
                    {
                        tenderTitle = tender.Title;
                        tenderReference = tender.ReferenceNumber;
                    }
                }
                else if (approval.TenderType == "Closed")
                {
                    var tender = await _context.ClosedTenders.FindAsync(approval.TenderId);
                    if (tender != null)
                    {
                        tenderTitle = tender.Title;
                        tenderReference = tender.ReferenceNumber;
                    }
                }

                // Send email notification to requester
                try
                {
                    await _emailService.SendApprovalNotificationEmailAsync(
                        approval.RequestedByUser.Email,
                        $"{approval.RequestedByUser.FirstName} {approval.RequestedByUser.LastName}".Trim(),
                        tenderTitle,
                        tenderReference,
                        approval.ApprovalStage,
                        approval.ApprovalStatus,
                        request.Comments ?? ""
                    );

                    _logger.LogInformation(
                        "Approval {Status}. Tender: {TenderReference}, Stage: {Stage}",
                        approval.ApprovalStatus, tenderReference, approval.ApprovalStage);
                }
                catch (Exception emailEx)
                {
                    _logger.LogError(emailEx, "Failed to send approval notification email");
                }

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error responding to approval");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/TenderApprovals/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<TenderApprovalEntity>> GetApproval(int id)
        {
            var approval = await _context.TenderApprovals
                .Include(a => a.ApproverUser)
                .Include(a => a.RequestedByUser)
                .Include(a => a.PreviousApproval)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (approval == null)
            {
                return NotFound();
            }

            return approval;
        }

        // GET: api/TenderApprovals/tender/{tenderId}/{tenderType}
        [HttpGet("tender/{tenderId}/{tenderType}")]
        public async Task<ActionResult<IEnumerable<TenderApprovalEntity>>> GetTenderApprovals(
            int tenderId,
            string tenderType,
            [FromQuery] string? status = null)
        {
            var query = _context.TenderApprovals
                .Include(a => a.ApproverUser)
                .Include(a => a.RequestedByUser)
                .Where(a => a.TenderId == tenderId && a.TenderType == tenderType);

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(a => a.ApprovalStatus == status);
            }

            var approvals = await query
                .OrderBy(a => a.OrderIndex)
                .ThenBy(a => a.RequestedDate)
                .ToListAsync();

            return Ok(approvals);
        }

        // GET: api/TenderApprovals/pending
        [HttpGet("pending")]
        public async Task<ActionResult<IEnumerable<TenderApprovalEntity>>> GetPendingApprovals()
        {
            var currentUsername = User.Identity?.Name;
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == currentUsername);

            if (user == null)
            {
                return Unauthorized();
            }

            var approvals = await _context.TenderApprovals
                .Include(a => a.RequestedByUser)
                .Where(a => a.ApproverUserId == user.Id && a.ApprovalStatus == "Pending")
                .OrderBy(a => a.RequestedDate)
                .ToListAsync();

            return Ok(approvals);
        }

        // GET: api/TenderApprovals/my-requests
        [HttpGet("my-requests")]
        public async Task<ActionResult<IEnumerable<TenderApprovalEntity>>> GetMyApprovalRequests(
            [FromQuery] string? status = null)
        {
            var currentUsername = User.Identity?.Name;
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == currentUsername);

            if (user == null)
            {
                return Unauthorized();
            }

            var query = _context.TenderApprovals
                .Include(a => a.ApproverUser)
                .Where(a => a.RequestedByUserId == user.Id);

            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(a => a.ApprovalStatus == status);
            }

            var approvals = await query
                .OrderByDescending(a => a.RequestedDate)
                .ToListAsync();

            return Ok(approvals);
        }

        // POST: api/TenderApprovals/workflow
        [HttpPost("workflow")]
        public async Task<ActionResult<IEnumerable<TenderApprovalEntity>>> CreateApprovalWorkflow(
            [FromBody] CreateApprovalWorkflowRequest request)
        {
            try
            {
                // Get current user
                var currentUsername = User.Identity?.Name;
                var requestedByUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == currentUsername);

                if (requestedByUser == null)
                {
                    return Unauthorized();
                }

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

                var approvals = new List<TenderApprovalEntity>();
                int orderIndex = 0;

                foreach (var stage in request.Stages)
                {
                    var approval = new TenderApprovalEntity
                    {
                        TenderId = request.TenderId,
                        TenderType = request.TenderType,
                        ApprovalStage = stage.StageName,
                        ApproverUserId = stage.ApproverUserId,
                        ApprovalStatus = "Pending",
                        RequestedDate = DateTime.UtcNow,
                        RequestDetails = stage.RequestDetails,
                        RequestedByUserId = requestedByUser.Id,
                        OrderIndex = orderIndex++,
                        IsRequired = stage.IsRequired
                    };

                    approvals.Add(approval);
                }

                _context.TenderApprovals.AddRange(approvals);
                await _context.SaveChangesAsync();

                _logger.LogInformation(
                    "Approval workflow created. TenderId: {TenderId}, Stages: {StageCount}",
                    request.TenderId, approvals.Count);

                return Ok(approvals);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating approval workflow");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // DELETE: api/TenderApprovals/{id}
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> DeleteApproval(int id)
        {
            var approval = await _context.TenderApprovals.FindAsync(id);

            if (approval == null)
            {
                return NotFound();
            }

            if (approval.ApprovalStatus != "Pending")
            {
                return BadRequest("Cannot delete approval that has already been processed");
            }

            _context.TenderApprovals.Remove(approval); // Soft delete via SaveChanges
            await _context.SaveChangesAsync();

            return NoContent();
        }
    }

    // DTOs
    public class RequestApprovalRequest
    {
        public int TenderId { get; set; }
        public string TenderType { get; set; } = "Live";
        public string ApprovalStage { get; set; } = string.Empty;
        public int ApproverUserId { get; set; }
        public string? RequestDetails { get; set; }
        public int OrderIndex { get; set; } = 0;
        public bool IsRequired { get; set; } = true;
    }

    public class ApprovalResponseRequest
    {
        public string Status { get; set; } = "Approved"; // Approved, Rejected, RequestChanges
        public string? Comments { get; set; }
    }

    public class CreateApprovalWorkflowRequest
    {
        public int TenderId { get; set; }
        public string TenderType { get; set; } = "Live";
        public List<ApprovalStage> Stages { get; set; } = new();
    }

    public class ApprovalStage
    {
        public string StageName { get; set; } = string.Empty;
        public int ApproverUserId { get; set; }
        public string? RequestDetails { get; set; }
        public bool IsRequired { get; set; } = true;
    }
}
