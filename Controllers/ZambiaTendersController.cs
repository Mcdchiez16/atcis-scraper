using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Models;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "EmployeeAccess")]
    [ApiExplorerSettings(GroupName = "zambia")]
    public class ZambiaTendersController : ControllerBase
    {
        private readonly IZambiaTenderScraperService _zambiaScraperService;
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly ILogger<ZambiaTendersController> _logger;

        public ZambiaTendersController(
            IZambiaTenderScraperService zambiaScraperService,
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            ILogger<ZambiaTendersController> logger)
        {
            _zambiaScraperService = zambiaScraperService;
            _context = context;
            _environment = environment;
            _logger = logger;
        }

        /// <summary>
        /// Scrape a single page of opened tenders from Zambia (ZPPA) portal.
        /// </summary>
        /// <param name="pageNumber">Page number to scrape (starts at 1)</param>
        [HttpGet("page/{pageNumber}")]
        public async Task<ActionResult<ApiResponse<ZambiaTenderBatch>>> GetPage(int pageNumber)
        {
            try
            {
                if (pageNumber < 1)
                {
                    return BadRequest(new ApiResponse<ZambiaTenderBatch>
                    {
                        Success = false,
                        Message = "Page number must be greater than or equal to 1."
                    });
                }

                var batch = await _zambiaScraperService.ScrapeOpenedTendersPageAsync(pageNumber);

                return Ok(new ApiResponse<ZambiaTenderBatch>
                {
                    Success = true,
                    Message = $"Successfully scraped Zambia tenders page {pageNumber}",
                    Data = batch,
                    TotalCount = batch.TotalTenders
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping Zambia tenders page {PageNumber}", pageNumber);
                return StatusCode(500, new ApiResponse<ZambiaTenderBatch>
                {
                    Success = false,
                    Message = $"Error scraping Zambia page: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Scrape multiple pages of opened tenders from Zambia (ZPPA) portal concurrently.
        /// </summary>
        /// <param name="start">Starting page number (default: 1)</param>
        /// <param name="end">Ending page number (default: 5)</param>
        [HttpGet("pages")]
        public async Task<ActionResult<ApiResponse<List<ZambiaTender>>>> GetMultiplePages(
            [FromQuery] int start = 1,
            [FromQuery] int end = 5)
        {
            try
            {
                if (start < 1 || end < start)
                {
                    return BadRequest(new ApiResponse<List<ZambiaTender>>
                    {
                        Success = false,
                        Message = "Invalid page range. Start must be >= 1 and end >= start."
                    });
                }

                var tenders = await _zambiaScraperService.ScrapeMultipleOpenedTendersPagesAsync(start, end);

                return Ok(new ApiResponse<List<ZambiaTender>>
                {
                    Success = true,
                    Message = $"Successfully scraped Zambia tenders for pages {start}-{end}",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping Zambia tenders for pages {Start}-{End}", start, end);
                return StatusCode(500, new ApiResponse<List<ZambiaTender>>
                {
                    Success = false,
                    Message = $"Error scraping Zambia pages: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Search opened tenders on Zambia portal by title, reference number, procuring entity, or procurement method.
        /// </summary>
        /// <param name="keyword">Keyword to search for</param>
        /// <param name="page">Result page number (default: 1)</param>
        /// <param name="maxPagesToScan">Max portal pages to scan (default: 5)</param>
        [HttpGet("search")]
        public async Task<ActionResult<ApiResponse<ZambiaTenderBatch>>> SearchTenders(
            [FromQuery] string keyword,
            [FromQuery] int page = 1,
            [FromQuery] int maxPagesToScan = 5)
        {
            try
            {
                var batch = await _zambiaScraperService.SearchOpenedTendersAsync(keyword, page, maxPagesToScan);

                return Ok(new ApiResponse<ZambiaTenderBatch>
                {
                    Success = true,
                    Message = $"Search completed. Found {batch.TotalTenders} matches for '{keyword}'",
                    Data = batch,
                    TotalCount = batch.TotalTenders
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching Zambia tenders with keyword {Keyword}", keyword);
                return StatusCode(500, new ApiResponse<ZambiaTenderBatch>
                {
                    Success = false,
                    Message = $"Error searching Zambia tenders: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Filter opened tenders from Zambia portal by procuring entity.
        /// </summary>
        /// <param name="entityName">Procuring entity name (e.g. ZESCO, Zamtel, Ministry)</param>
        /// <param name="maxPagesToScan">Max portal pages to scan (default: 5)</param>
        [HttpGet("by-entity")]
        public async Task<ActionResult<ApiResponse<List<ZambiaTender>>>> GetByEntity(
            [FromQuery] string entityName,
            [FromQuery] int maxPagesToScan = 5)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(entityName))
                {
                    return BadRequest(new ApiResponse<List<ZambiaTender>>
                    {
                        Success = false,
                        Message = "entityName parameter is required."
                    });
                }

                var tenders = await _zambiaScraperService.GetTendersByEntityAsync(entityName, maxPagesToScan);

                return Ok(new ApiResponse<List<ZambiaTender>>
                {
                    Success = true,
                    Message = $"Found {tenders.Count} tenders for procuring entity '{entityName}'",
                    Data = tenders,
                    TotalCount = tenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error filtering Zambia tenders by entity {EntityName}", entityName);
                return StatusCode(500, new ApiResponse<List<ZambiaTender>>
                {
                    Success = false,
                    Message = $"Error filtering Zambia tenders: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get total number of pages available on the Zambia ZPPA portal.
        /// </summary>
        [HttpGet("total-pages")]
        public async Task<ActionResult<ApiResponse<int>>> GetTotalPages()
        {
            try
            {
                var totalPages = await _zambiaScraperService.GetTotalPagesAsync();
                return Ok(new ApiResponse<int>
                {
                    Success = true,
                    Message = $"Total pages available on Zambia portal: {totalPages}",
                    Data = totalPages,
                    TotalCount = totalPages
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting total pages from Zambia portal");
                return StatusCode(500, new ApiResponse<int>
                {
                    Success = false,
                    Message = $"Error retrieving total pages: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get full tender details, metadata, deadlines, fees, lots, and attached documents by resource ID.
        /// </summary>
        /// <param name="resourceId">The ZPPA tender resourceId (e.g. 29646841)</param>
        [HttpGet("details/{resourceId}")]
        public async Task<ActionResult<ApiResponse<ZambiaTenderDetailDto>>> GetTenderDetails(string resourceId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(resourceId))
                {
                    return BadRequest(new ApiResponse<ZambiaTenderDetailDto>
                    {
                        Success = false,
                        Message = "resourceId parameter is required."
                    });
                }

                var details = await _zambiaScraperService.GetTenderDetailsAsync(resourceId);

                return Ok(new ApiResponse<ZambiaTenderDetailDto>
                {
                    Success = true,
                    Message = $"Successfully retrieved tender details for {resourceId}",
                    Data = details
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tender details for resource {ResourceId}", resourceId);
                return StatusCode(500, new ApiResponse<ZambiaTenderDetailDto>
                {
                    Success = false,
                    Message = $"Error retrieving tender details: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get attached contract documents, solicitation PDFs, and BOQ spreadsheets for a tender by resource ID.
        /// </summary>
        /// <param name="resourceId">The ZPPA tender resourceId (e.g. 29646841)</param>
        [HttpGet("documents/{resourceId}")]
        public async Task<ActionResult<ApiResponse<List<ZambiaTenderDocumentDto>>>> GetTenderDocuments(string resourceId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(resourceId))
                {
                    return BadRequest(new ApiResponse<List<ZambiaTenderDocumentDto>>
                    {
                        Success = false,
                        Message = "resourceId parameter is required."
                    });
                }

                var documents = await _zambiaScraperService.GetTenderDocumentsAsync(resourceId);

                return Ok(new ApiResponse<List<ZambiaTenderDocumentDto>>
                {
                    Success = true,
                    Message = $"Successfully retrieved {documents.Count} documents for tender {resourceId}",
                    Data = documents,
                    TotalCount = documents.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tender documents for resource {ResourceId}", resourceId);
                return StatusCode(500, new ApiResponse<List<ZambiaTenderDocumentDto>>
                {
                    Success = false,
                    Message = $"Error retrieving tender documents: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Browse published annual procurement plans and document download links from Zambia ZPPA.
        /// </summary>
        /// <param name="page">Page number (default: 1)</param>
        /// <param name="cycleId">Optional cycle ID (e.g. 1012 for 2026)</param>
        [HttpGet("publications")]
        public async Task<ActionResult<ApiResponse<ZambiaPublicationBatch>>> GetPublications(
            [FromQuery] int page = 1,
            [FromQuery] string? cycleId = null)
        {
            try
            {
                var batch = await _zambiaScraperService.ScrapePublishedProcurementPlansAsync(page, cycleId);
                return Ok(new ApiResponse<ZambiaPublicationBatch>
                {
                    Success = true,
                    Message = $"Successfully retrieved published plans for cycle {batch.CycleId}, page {page}",
                    Data = batch,
                    TotalCount = batch.TotalItems
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting published procurement plans");
                return StatusCode(500, new ApiResponse<ZambiaPublicationBatch>
                {
                    Success = false,
                    Message = $"Error getting published plans: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Download or stream a remote Zambia tender document, PDF, or spreadsheet with local caching.
        /// </summary>
        /// <param name="url">The remote download URL from ZPPA portal</param>
        [HttpGet("document/download")]
        public async Task<IActionResult> DownloadDocument([FromQuery] string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    return BadRequest("URL parameter is required.");
                }

                var (fileBytes, contentType, fileName) = await _zambiaScraperService.DownloadDocumentFileAsync(url);
                return File(fileBytes, contentType, fileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading document from {Url}", url);
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Failed downloading document: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Analyze a Zambia tender document or scope with Gemini AI to extract key requirements, eligibility, and checklist items.
        /// </summary>
        [HttpPost("analyze-document")]
        public async Task<ActionResult<ApiResponse<ZambiaDocumentAnalysisResponse>>> AnalyzeDocument(
            [FromBody] ZambiaDocumentAnalysisRequest request)
        {
            try
            {
                var result = await _zambiaScraperService.AnalyzeZambiaDocumentAsync(request);
                return Ok(new ApiResponse<ZambiaDocumentAnalysisResponse>
                {
                    Success = true,
                    Message = "Document analysis generated successfully.",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing Zambia document");
                return StatusCode(500, new ApiResponse<ZambiaDocumentAnalysisResponse>
                {
                    Success = false,
                    Message = $"Error analyzing document: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Attach a downloaded or scraped Zambia document directly into the system's Tender Documents database.
        /// </summary>
        [HttpPost("attach-document")]
        public async Task<ActionResult<ApiResponse<TenderDocumentEntity>>> AttachDocument(
            [FromBody] AttachZambiaDocumentRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.RemoteFileUrl))
                {
                    return BadRequest(new ApiResponse<TenderDocumentEntity>
                    {
                        Success = false,
                        Message = "RemoteFileUrl is required."
                    });
                }

                var currentUsername = User.Identity?.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == currentUsername);
                if (user == null)
                {
                    return Unauthorized(new ApiResponse<TenderDocumentEntity>
                    {
                        Success = false,
                        Message = "User not found."
                    });
                }

                var (fileBytes, contentType, fileName) = await _zambiaScraperService.DownloadDocumentFileAsync(request.RemoteFileUrl);
                var docName = !string.IsNullOrWhiteSpace(request.DocumentName) ? request.DocumentName : fileName;

                var tenderIdFolder = request.TargetTenderId?.ToString() ?? "zambia";
                var uploadsPath = Path.Combine(_environment.ContentRootPath, "uploads", "tenders", tenderIdFolder);
                Directory.CreateDirectory(uploadsPath);

                var fileExtension = Path.GetExtension(fileName);
                if (string.IsNullOrWhiteSpace(fileExtension)) fileExtension = ".bin";

                var uniqueFileName = $"{Guid.NewGuid()}{fileExtension}";
                var filePath = Path.Combine(uploadsPath, uniqueFileName);

                await System.IO.File.WriteAllBytesAsync(filePath, fileBytes);

                var docEntity = new TenderDocumentEntity
                {
                    TenderId = request.TargetTenderId ?? 0,
                    TenderType = "Zambia",
                    DocumentName = docName,
                    DocumentType = request.DocumentType,
                    FilePath = filePath,
                    FileExtension = fileExtension,
                    FileSize = fileBytes.Length,
                    MimeType = contentType,
                    Description = request.Description ?? $"Imported from Zambia ZPPA: {request.RemoteFileUrl}",
                    UploadedByUserId = user.Id,
                    UploadedAt = DateTime.UtcNow,
                    DocumentVersion = "1.0",
                    IsLatestVersion = true
                };

                _context.TenderDocuments.Add(docEntity);
                await _context.SaveChangesAsync();

                return Ok(new ApiResponse<TenderDocumentEntity>
                {
                    Success = true,
                    Message = $"Document '{docName}' successfully attached and saved.",
                    Data = docEntity
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error attaching Zambia document");
                return StatusCode(500, new ApiResponse<TenderDocumentEntity>
                {
                    Success = false,
                    Message = $"Error attaching document: {ex.Message}"
                });
            }
        }
    }
}
