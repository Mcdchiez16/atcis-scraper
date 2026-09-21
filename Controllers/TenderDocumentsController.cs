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
    public class TenderDocumentsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TenderDocumentsController> _logger;
        private readonly IWebHostEnvironment _environment;

        public TenderDocumentsController(
            ApplicationDbContext context,
            ILogger<TenderDocumentsController> logger,
            IWebHostEnvironment environment)
        {
            _context = context;
            _logger = logger;
            _environment = environment;
        }

        // Request model for multipart/form-data upload
        public class UploadTenderDocumentRequest
        {
            public int tenderId { get; set; }
            public string tenderType { get; set; }
            public string documentType { get; set; }
            public string? description { get; set; }
            public IFormFile file { get; set; }
        }

        // POST: api/TenderDocuments/upload
        [HttpPost("upload")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(52428800)] // 50MB limit
        public async Task<ActionResult<TenderDocumentEntity>> UploadDocument([FromForm] UploadTenderDocumentRequest form)
        {
            try
            {
                if (form.file == null || form.file.Length == 0)
                {
                    return BadRequest("No file provided");
                }

                // Get current user
                var currentUsername = User.Identity?.Name;
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == currentUsername);

                if (user == null)
                {
                    return Unauthorized("User not found");
                }

                // Verify tender exists
                var tenderExists = false;
                if (form.tenderType == "Live")
                {
                    tenderExists = await _context.LiveTenders.AnyAsync(t => t.Id == form.tenderId);
                }
                else if (form.tenderType == "Closed")
                {
                    tenderExists = await _context.ClosedTenders.AnyAsync(t => t.Id == form.tenderId);
                }

                if (!tenderExists)
                {
                    return NotFound($"{form.tenderType} tender with ID {form.tenderId} not found");
                }

                // Create uploads directory if it doesn't exist
                var uploadsPath = Path.Combine(_environment.ContentRootPath, "uploads", "tenders", form.tenderId.ToString());
                Directory.CreateDirectory(uploadsPath);

                // Generate unique filename
                var fileExtension = Path.GetExtension(form.file.FileName);
                var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsPath, uniqueFileName);

                // Save file
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await form.file.CopyToAsync(stream);
                }

                // Create document record
                var document = new TenderDocumentEntity
                {
                    TenderId = form.tenderId,
                    TenderType = form.tenderType,
                    DocumentName = form.file.FileName,
                    DocumentType = form.documentType,
                    FilePath = filePath,
                    FileExtension = fileExtension,
                    FileSize = form.file.Length,
                    MimeType = form.file.ContentType,
                    Description = form.description,
                    UploadedByUserId = user.Id,
                    UploadedAt = DateTime.UtcNow,
                    DocumentVersion = "1.0",
                    IsLatestVersion = true
                };

                _context.TenderDocuments.Add(document);
                await _context.SaveChangesAsync();

                // Load navigation properties
                await _context.Entry(document).Reference(d => d.UploadedByUser).LoadAsync();

                _logger.LogInformation(
                    "Document uploaded. TenderId: {TenderId}, Type: {DocumentType}, User: {Username}",
                    form.tenderId, form.documentType, currentUsername);

                return CreatedAtAction(nameof(GetDocument), new { id = document.Id }, document);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading document");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/TenderDocuments/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<TenderDocumentEntity>> GetDocument(int id)
        {
            var document = await _context.TenderDocuments
                .Include(d => d.UploadedByUser)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (document == null)
            {
                return NotFound();
            }

            return document;
        }

        // GET: api/TenderDocuments/tender/{tenderId}/{tenderType}
        [HttpGet("tender/{tenderId}/{tenderType}")]
        public async Task<ActionResult<IEnumerable<TenderDocumentEntity>>> GetTenderDocuments(
            int tenderId,
            string tenderType,
            [FromQuery] string? documentType = null)
        {
            var query = _context.TenderDocuments
                .Include(d => d.UploadedByUser)
                .Where(d => d.TenderId == tenderId && d.TenderType == tenderType);

            if (!string.IsNullOrEmpty(documentType))
            {
                query = query.Where(d => d.DocumentType == documentType);
            }

            var documents = await query
                .OrderByDescending(d => d.UploadedAt)
                .ToListAsync();

            return Ok(documents);
        }

        // GET: api/TenderDocuments/{id}/download
        [HttpGet("{id}/download")]
        public async Task<IActionResult> DownloadDocument(int id)
        {
            var document = await _context.TenderDocuments.FindAsync(id);

            if (document == null)
            {
                return NotFound();
            }

            if (!System.IO.File.Exists(document.FilePath))
            {
                return NotFound("File not found on server");
            }

            var fileBytes = await System.IO.File.ReadAllBytesAsync(document.FilePath);
            return File(fileBytes, document.MimeType ?? "application/octet-stream", document.DocumentName);
        }

        // PUT: api/TenderDocuments/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateDocument(int id, [FromBody] UpdateDocumentRequest request)
        {
            var document = await _context.TenderDocuments.FindAsync(id);

            if (document == null)
            {
                return NotFound();
            }

            document.DocumentType = request.DocumentType ?? document.DocumentType;
            document.Description = request.Description;

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // DELETE: api/TenderDocuments/{id}
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin,Manager")]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            var document = await _context.TenderDocuments.FindAsync(id);

            if (document == null)
            {
                return NotFound();
            }

            try
            {
                // Delete physical file
                if (System.IO.File.Exists(document.FilePath))
                {
                    System.IO.File.Delete(document.FilePath);
                }

                // Delete database record (soft delete via SaveChanges)
                _context.TenderDocuments.Remove(document);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Document deleted. DocumentId: {DocumentId}", id);

                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting document");
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        // GET: api/TenderDocuments/types
        [HttpGet("types")]
        public ActionResult<IEnumerable<string>> GetDocumentTypes()
        {
            var documentTypes = new[]
            {
                "PRAZ Certificate",
                "Tax Clearance",
                "Company Registration",
                "Technical Proposal",
                "Financial Proposal",
                "Tender Response",
                "Supporting Document",
                "Certificate of Compliance",
                "Bank Statement",
                "Reference Letter",
                "Other"
            };

            return Ok(documentTypes);
        }
    }

    // DTOs
    public class UpdateDocumentRequest
    {
        public string? DocumentType { get; set; }
        public string? Description { get; set; }
    }
}
