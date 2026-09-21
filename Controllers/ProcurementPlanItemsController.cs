using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    /// <summary>
    /// Controller for scraping and managing procurement plan items
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [ApiExplorerSettings(GroupName = "procurement")]
    public class ProcurementPlanItemsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ITenderScraperService _scraperService;
        private readonly IDatabaseSyncService _syncService;
        private readonly ILogger<ProcurementPlanItemsController> _logger;

        public ProcurementPlanItemsController(
            ApplicationDbContext context,
            ITenderScraperService scraperService,
            IDatabaseSyncService syncService,
            ILogger<ProcurementPlanItemsController> logger)
        {
            _context = context;
            _scraperService = scraperService;
            _syncService = syncService;
            _logger = logger;
        }

        /// <summary>
        /// Scrape and save procurement plan items for a specific plan
        /// </summary>
        /// <param name="planId">The database ID of the procurement plan</param>
        [HttpPost("scrape/{planId}")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<SyncResult>), 200)]
        public async Task<ActionResult<ApiResponse<SyncResult>>> ScrapePlanItems(int planId)
        {
            try
            {
                var plan = await _context.ProcurementPlans.FindAsync(planId);

                if (plan == null)
                {
                    return NotFound(new ApiResponse<SyncResult>
                    {
                        Success = false,
                        Message = "Procurement plan not found"
                    });
                }

                if (string.IsNullOrWhiteSpace(plan.ViewAppUrl))
                {
                    return BadRequest(new ApiResponse<SyncResult>
                    {
                        Success = false,
                        Message = "Plan has no ViewAppUrl to scrape from"
                    });
                }

                _logger.LogInformation($"Scraping items for plan {planId}: {plan.ProcuringEntity} - {plan.Year}");

                // Scrape the plan details including items
                var planDetail = await _scraperService.GetAnnualProcurementPlanDetailAsync(plan.ViewAppUrl);

                if (planDetail == null || planDetail.Items == null || planDetail.Items.Count == 0)
                {
                    return Ok(new ApiResponse<SyncResult>
                    {
                        Success = true,
                        Message = "No items found for this plan",
                        Data = new SyncResult
                        {
                            JobType = "ProcurementPlanItems",
                            StartTime = DateTime.UtcNow,
                            EndTime = DateTime.UtcNow,
                            Success = true,
                            ItemsProcessed = 0,
                            Summary = "No items found"
                        }
                    });
                }

                // Sync the items to database
                var result = await _syncService.SyncProcurementPlanItemsAsync(planId, planDetail.Items);

                return Ok(new ApiResponse<SyncResult>
                {
                    Success = result.Success,
                    Message = result.Success ? "Items scraped and saved successfully" : "Some items failed to sync",
                    Data = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error scraping items for plan {planId}");
                return StatusCode(500, new ApiResponse<SyncResult>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Scrape and save items for all procurement plans
        /// </summary>
        [HttpPost("scrape-all")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        [ProducesResponseType(typeof(ApiResponse<List<SyncResult>>), 200)]
        public async Task<ActionResult<ApiResponse<List<SyncResult>>>> ScrapeAllPlanItems()
        {
            try
            {
                var plans = await _context.ProcurementPlans
                    .Where(p => !string.IsNullOrEmpty(p.ViewAppUrl) && !p.IsDeleted)
                    .ToListAsync();

                _logger.LogInformation($"Scraping items for {plans.Count} procurement plans");

                var results = new List<SyncResult>();

                foreach (var plan in plans)
                {
                    try
                    {
                        _logger.LogInformation($"Processing plan {plan.Id}: {plan.ProcuringEntity} - {plan.Year}");

                        var planDetail = await _scraperService.GetAnnualProcurementPlanDetailAsync(plan.ViewAppUrl);

                        if (planDetail != null && planDetail.Items != null && planDetail.Items.Any())
                        {
                            var result = await _syncService.SyncProcurementPlanItemsAsync(plan.Id, planDetail.Items);
                            results.Add(result);
                        }
                        else
                        {
                            results.Add(new SyncResult
                            {
                                JobType = $"ProcurementPlanItems-{plan.Id}",
                                StartTime = DateTime.UtcNow,
                                EndTime = DateTime.UtcNow,
                                Success = true,
                                ItemsProcessed = 0,
                                Summary = $"No items found for {plan.ProcuringEntity}"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error processing plan {plan.Id}");
                        results.Add(new SyncResult
                        {
                            JobType = $"ProcurementPlanItems-{plan.Id}",
                            StartTime = DateTime.UtcNow,
                            EndTime = DateTime.UtcNow,
                            Success = false,
                            ItemsFailed = 1,
                            Errors = new List<string> { ex.Message },
                            Summary = $"Failed: {ex.Message}"
                        });
                    }
                }

                var totalProcessed = results.Sum(r => r.ItemsProcessed);
                var totalAdded = results.Sum(r => r.ItemsAdded);
                var totalUpdated = results.Sum(r => r.ItemsUpdated);
                var totalFailed = results.Sum(r => r.ItemsFailed);

                return Ok(new ApiResponse<List<SyncResult>>
                {
                    Success = true,
                    Message = $"Processed {plans.Count} plans: {totalAdded} items added, {totalUpdated} updated, {totalFailed} failed",
                    Data = results,
                    TotalCount = results.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping all plan items");
                return StatusCode(500, new ApiResponse<List<SyncResult>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get all items for a specific procurement plan
        /// </summary>
        [HttpGet("plan/{planId}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<List<ProcurementPlanItem>>), 200)]
        public async Task<ActionResult<ApiResponse<List<ProcurementPlanItem>>>> GetPlanItems(int planId)
        {
            try
            {
                var items = await _context.ProcurementPlanItems
                    .Where(i => i.ProcurementPlanId == planId && !i.IsDeleted)
                    .OrderBy(i => i.RefNo)
                    .ToListAsync();

                var itemDtos = items.Select(i => new ProcurementPlanItem
                {
                    ItemId = i.ItemId,
                    RefNo = i.RefNo,
                    ClassOfProcurement = i.ClassOfProcurement,
                    ObjectCode = i.ObjectCode,
                    Description = i.Description,
                    PmoEndUser = i.PmoEndUser,
                    ProcurementMethod = i.ProcurementMethod,
                    EoiPublicationDate = i.EoiPublicationDate,
                    EoiClosingDate = i.EoiClosingDate,
                    TenderPublicationDate = i.TenderPublicationDate,
                    BidClosingDate = i.BidClosingDate,
                    AwardNoticeDate = i.AwardNoticeDate,
                    ContractSigningDate = i.ContractSigningDate,
                    CycleDays = i.CycleDays,
                    LeadTime = i.LeadTime,
                    Spoc = i.Spoc,
                    SourceOfFunds = i.SourceOfFunds,
                    UnitOfMeasurement = i.UnitOfMeasurement,
                    Quantity = i.Quantity,
                    Comments = i.Comments,
                    IsSupplement = i.IsSupplement,
                    ParsedTenderPublicationDate = i.ParsedTenderPublicationDate,
                    ParsedBidClosingDate = i.ParsedBidClosingDate,
                    ParsedAwardNoticeDate = i.ParsedAwardNoticeDate,
                    ParsedContractSigningDate = i.ParsedContractSigningDate
                }).ToList();

                return Ok(new ApiResponse<List<ProcurementPlanItem>>
                {
                    Success = true,
                    Data = itemDtos,
                    TotalCount = itemDtos.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving items for plan {planId}");
                return StatusCode(500, new ApiResponse<List<ProcurementPlanItem>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get items by procuring entity
        /// </summary>
        [HttpGet("entity/{entityName}")]
        [AllowAnonymous]
        [ProducesResponseType(typeof(ApiResponse<List<ProcurementPlanItem>>), 200)]
        public async Task<ActionResult<ApiResponse<List<ProcurementPlanItem>>>> GetItemsByEntity(string entityName)
        {
            try
            {
                var items = await _context.ProcurementPlanItems
                    .Include(i => i.ProcurementPlan)
                    .Where(i => i.ProcurementPlan.ProcuringEntity.Contains(entityName) && !i.IsDeleted)
                    .OrderBy(i => i.ProcurementPlan.Year)
                    .ThenBy(i => i.RefNo)
                    .Take(500) // Limit results
                    .ToListAsync();

                var itemDtos = items.Select(i => new ProcurementPlanItem
                {
                    ItemId = i.ItemId,
                    RefNo = i.RefNo,
                    Description = i.Description,
                    PmoEndUser = i.PmoEndUser,
                    ProcurementMethod = i.ProcurementMethod,
                    TenderPublicationDate = i.TenderPublicationDate,
                    BidClosingDate = i.BidClosingDate,
                    ParsedTenderPublicationDate = i.ParsedTenderPublicationDate,
                    ParsedBidClosingDate = i.ParsedBidClosingDate
                }).ToList();

                return Ok(new ApiResponse<List<ProcurementPlanItem>>
                {
                    Success = true,
                    Data = itemDtos,
                    TotalCount = itemDtos.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving items for entity {entityName}");
                return StatusCode(500, new ApiResponse<List<ProcurementPlanItem>>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }
    }
}
