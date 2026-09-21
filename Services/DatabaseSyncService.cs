using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.Data.Repositories;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Models;

namespace ZimbabweTenderAPI.Services
{
    public interface IDatabaseSyncService
    {
        Task<SyncResult> SyncLiveTendersAsync(List<Tender> tenders);
        Task<SyncResult> SyncClosedTendersAsync(List<Tender> tenders);
        Task<SyncResult> SyncAwardNoticesAsync(List<AwardNoticeDto> awards);
        Task<SyncResult> SyncProcurementPlansAsync(List<AnnualProcurementPlanDto> plans);
        Task<SyncResult> SyncProcurementPlanItemsAsync(int procurementPlanId, List<ProcurementPlanItem> items);
        Task<SyncResult> SyncProcurementPlanWithItemsAsync(AnnualProcurementPlanDetailDto planDetail);
        Task<SyncResult> VerifyAndMoveTendersAsync();
        Task<List<ProcurementPlanEntity>> GetAllProcurementPlansWithUrlsAsync();
    }

    public class DatabaseSyncService : IDatabaseSyncService
    {
        private readonly ApplicationDbContext _context;
        private readonly ITenderRepository _tenderRepo;
        private readonly ILogger<DatabaseSyncService> _logger;
        private readonly string _logPath = "C:/Temp/tender_debug2db.txt";
        private readonly string _errorLogPath = "C:/Temp/tender_errors_detailed.txt";

        public DatabaseSyncService(
            ApplicationDbContext context,
            ITenderRepository tenderRepo,
            ILogger<DatabaseSyncService> logger)
        {
            _context = context;
            _tenderRepo = tenderRepo;
            _logger = logger;

            // Ensure directory exists
            try { Directory.CreateDirectory("C:/Temp"); } catch { }
        }

        public async Task<List<ProcurementPlanEntity>> GetAllProcurementPlansWithUrlsAsync()
        {
            return await _context.ProcurementPlans
                .Where(p => !string.IsNullOrEmpty(p.ViewAppUrl))
                .OrderBy(p => p.Id)
                .ToListAsync();
        }

        private void TrackDBProgress(string message)
        {
            try
            {
                string logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
                File.AppendAllText(_logPath, logEntry);
            }
            catch { /* Avoid crashing the app if file is locked */ }
        }

        private void LogDetailedError(string operation, Exception ex, object entity = null)
        {
            try
            {
                var errorLog = new
                {
                    Timestamp = DateTime.Now,
                    Operation = operation,
                    ErrorMessage = ex.Message,
                    InnerException = ex.InnerException?.Message,
                    InnerInnerException = ex.InnerException?.InnerException?.Message,
                    StackTrace = ex.StackTrace,
                    EntitySample = entity != null ? TruncateForLog(JsonSerializer.Serialize(entity)) : null
                };

                string logEntry = JsonSerializer.Serialize(errorLog, new JsonSerializerOptions
                {
                    WriteIndented = true
                }) + Environment.NewLine + "===================" + Environment.NewLine;

                File.AppendAllText(_errorLogPath, logEntry);

                _logger.LogError(ex, $"DETAILED ERROR in {operation}: {ex.Message} | Inner: {ex.InnerException?.Message} | InnerInner: {ex.InnerException?.InnerException?.Message}");
            }
            catch { }
        }

        private string TruncateForLog(string text, int maxLength = 500)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;
            return text.Substring(0, maxLength) + "... (truncated)";
        }

        private string SafeTruncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        public async Task<SyncResult> SyncLiveTendersAsync(List<Tender> tenders)
        {
            var result = new SyncResult { JobType = "LiveTenders", StartTime = DateTime.UtcNow };
            TrackDBProgress($"START DB SYNC: Processing {tenders.Count} Live Tenders");

            try
            {
                if (tenders.Count == 0)
                {
                    TrackDBProgress("SKIP: No live tenders received.");
                    result.EndTime = DateTime.UtcNow;
                    result.Success = true;
                    result.Summary = "No live tenders to process";
                    await SaveJobHistoryAsync(result);
                    return result;
                }

                // Deduplicate by TenderId - keep the first occurrence
                var originalCount = tenders.Count;
                tenders = tenders
                    .Where(t => !string.IsNullOrEmpty(t.TenderId))
                    .GroupBy(t => t.TenderId)
                    .Select(g => g.First())
                    .ToList();

                if (originalCount != tenders.Count)
                {
                    var duplicateCount = originalCount - tenders.Count;
                    TrackDBProgress($"DEDUP: Removed {duplicateCount} duplicate TenderIds from scraped data");
                    _logger.LogWarning($"Removed {duplicateCount} duplicate TenderIds from live tenders batch");
                }

                foreach (var tender in tenders)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(tender.TenderId))
                        {
                            result.ItemsFailed++;
                            TrackDBProgress($"SKIP: Empty TenderId");
                            continue;
                        }

                        var entity = MapToLiveTenderEntity(tender);
                        await _tenderRepo.UpsertAsync(entity, "System");

                        result.ItemsProcessed++;
                        if (entity.Id == 0) result.ItemsAdded++;
                        else result.ItemsUpdated++;

                        // Log progress every 100 items
                        if (result.ItemsProcessed % 100 == 0)
                        {
                            TrackDBProgress($"PROGRESS: Processed {result.ItemsProcessed} live tenders");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ItemsFailed++;
                        LogDetailedError($"SyncLiveTender-{tender.TenderId}", ex, tender);
                        result.Errors.Add($"TenderId {tender.TenderId}: {ex.Message}");
                    }
                }

                result.EndTime = DateTime.UtcNow;
                result.Success = result.ItemsFailed == 0;
                result.Summary = $"Processed {result.ItemsProcessed}: {result.ItemsAdded} added, {result.ItemsUpdated} updated, {result.ItemsFailed} failed";

                await SaveJobHistoryAsync(result);
                TrackDBProgress($"FINISH DB SYNC: {result.Summary}");

                return result;
            }
            catch (Exception ex)
            {
                LogDetailedError("SyncLiveTendersAsync", ex);
                TrackDBProgress($"FATAL DB ERROR (Live): {ex.Message} | Inner: {ex.InnerException?.Message}");
                result.Success = false;
                result.Errors.Add(ex.Message);
                result.Errors.Add(ex.InnerException?.Message ?? "No inner exception");
                return result;
            }
        }

        public async Task<SyncResult> SyncClosedTendersAsync(List<Tender> tenders)
        {
            var result = new SyncResult { JobType = "ClosedTenders", StartTime = DateTime.UtcNow };
            TrackDBProgress($"START DB SYNC: Processing {tenders.Count} Closed Tenders");

            try
            {
                if (tenders.Count == 0)
                {
                    TrackDBProgress("SKIP: No closed tenders received.");
                    result.EndTime = DateTime.UtcNow;
                    result.Success = true;
                    result.Summary = "No closed tenders to process";
                    await SaveJobHistoryAsync(result);
                    return result;
                }

                // Deduplicate by TenderId - keep the first occurrence
                var originalCount = tenders.Count;
                tenders = tenders
                    .Where(t => !string.IsNullOrEmpty(t.TenderId))
                    .GroupBy(t => t.TenderId)
                    .Select(g => g.First())
                    .ToList();

                if (originalCount != tenders.Count)
                {
                    var duplicateCount = originalCount - tenders.Count;
                    TrackDBProgress($"DEDUP: Removed {duplicateCount} duplicate TenderIds from scraped data");
                    _logger.LogWarning($"Removed {duplicateCount} duplicate TenderIds from closed tenders batch");
                }

                foreach (var tender in tenders)
                {
                    try
                    {
                        // Validate required fields
                        if (string.IsNullOrEmpty(tender.TenderId))
                        {
                            result.ItemsFailed++;
                            TrackDBProgress($"SKIP: Empty TenderId");
                            continue;
                        }

                        // Check if exists
                        var existing = await _context.ClosedTenders
                            .FirstOrDefaultAsync(t => t.TenderId == tender.TenderId);

                        if (existing == null)
                        {
                            var entity = MapToClosedTenderEntity(tender);
                            await _context.ClosedTenders.AddAsync(entity);
                            result.ItemsAdded++;
                        }
                        else
                        {
                            UpdateClosedTenderEntity(existing, tender);
                            result.ItemsUpdated++;
                        }

                        result.ItemsProcessed++;

                        // Save in batches of 100 to avoid memory issues and provide better error isolation
                        if (result.ItemsProcessed % 100 == 0)
                        {
                            await _context.SaveChangesAsync();
                            TrackDBProgress($"BATCH SAVE: Saved {result.ItemsProcessed} closed tenders");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ItemsFailed++;
                        LogDetailedError($"SyncClosedTender-{tender.TenderId}", ex, tender);
                        result.Errors.Add($"TenderId {tender.TenderId}: {ex.Message}");
                    }
                }

                // Final save for remaining items
                if (result.ItemsAdded > 0 || result.ItemsUpdated > 0)
                {
                    await _context.SaveChangesAsync();
                }

                result.EndTime = DateTime.UtcNow;
                result.Success = result.ItemsFailed == 0;
                result.Summary = $"Processed {result.ItemsProcessed}: {result.ItemsAdded} added, {result.ItemsUpdated} updated, {result.ItemsFailed} failed";

                await SaveJobHistoryAsync(result);
                TrackDBProgress($"FINISH DB SYNC: {result.Summary}");
                return result;
            }
            catch (Exception ex)
            {
                LogDetailedError("SyncClosedTendersAsync", ex);
                TrackDBProgress($"FATAL DB ERROR (Closed): {ex.Message} | Inner: {ex.InnerException?.Message} | InnerInner: {ex.InnerException?.InnerException?.Message}");
                result.Success = false;
                result.Errors.Add(ex.Message);
                result.Errors.Add(ex.InnerException?.Message ?? "No inner exception");
                result.Errors.Add(ex.InnerException?.InnerException?.Message ?? "No inner inner exception");
                return result;
            }
        }

        public async Task<SyncResult> SyncAwardNoticesAsync(List<AwardNoticeDto> awards)
        {
            var result = new SyncResult { JobType = "AwardNotices", StartTime = DateTime.UtcNow };
            TrackDBProgress($"START DB SYNC: Processing {awards.Count} Award Notices");

            try
            {
                if (awards.Count == 0)
                {
                    TrackDBProgress("SKIP: No award notices received.");
                    result.EndTime = DateTime.UtcNow;
                    result.Success = true;
                    result.Summary = "No award notices to process";
                    await SaveJobHistoryAsync(result);
                    return result;
                }

                // Deduplicate by AwardNoticeNumber - keep the first occurrence
                var originalCount = awards.Count;
                awards = awards
                    .Where(a => !string.IsNullOrEmpty(a.AwardNoticeNumber))
                    .GroupBy(a => a.AwardNoticeNumber)
                    .Select(g => g.First())
                    .ToList();

                if (originalCount != awards.Count)
                {
                    var duplicateCount = originalCount - awards.Count;
                    TrackDBProgress($"DEDUP: Removed {duplicateCount} duplicate AwardNoticeNumbers from scraped data");
                    _logger.LogWarning($"Removed {duplicateCount} duplicate AwardNoticeNumbers from award notices batch");
                }

                foreach (var award in awards)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(award.AwardNoticeNumber))
                        {
                            result.ItemsFailed++;
                            TrackDBProgress($"SKIP: Empty AwardNoticeNumber");
                            continue;
                        }

                        // Detach any tracked entities for this award to prevent conflicts
                        var trackedEntities = _context.ChangeTracker.Entries<AwardNoticeEntity>()
                            .Where(e => e.Entity.AwardNoticeNumber == award.AwardNoticeNumber)
                            .ToList();

                        foreach (var tracked in trackedEntities)
                        {
                            _context.Entry(tracked.Entity).State = EntityState.Detached;
                        }

                        // Now check if it exists in database
                        var existing = await _context.AwardNotices
                            .AsNoTracking() // Don't track this query
                            .FirstOrDefaultAsync(a => a.AwardNoticeNumber == award.AwardNoticeNumber);

                        if (existing == null)
                        {
                            var entity = MapToAwardNoticeEntity(award);
                            
                            // ENHANCEMENT: Try to link award to tender
                            await LinkAwardToTenderAsync(entity, award);
                            
                            await _context.AwardNotices.AddAsync(entity);
                            result.ItemsAdded++;
                        }
                        else
                        {
                            // Load the entity for updating
                            var entityToUpdate = await _context.AwardNotices
                                .FirstOrDefaultAsync(a => a.AwardNoticeNumber == award.AwardNoticeNumber);

                            if (entityToUpdate != null)
                            {
                                UpdateAwardNoticeEntity(entityToUpdate, award);
                                
                                // ENHANCEMENT: Try to link award to tender
                                await LinkAwardToTenderAsync(entityToUpdate, award);
                                
                                result.ItemsUpdated++;
                            }
                        }

                        result.ItemsProcessed++;

                        // Save in batches to avoid large transactions
                        if (result.ItemsProcessed % 50 == 0) // Changed from 100 to 50 for awards
                        {
                            await _context.SaveChangesAsync();

                            // Clear change tracker to prevent memory issues
                            _context.ChangeTracker.Clear();

                            TrackDBProgress($"BATCH SAVE: Saved {result.ItemsProcessed} award notices");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ItemsFailed++;
                        LogDetailedError($"SyncAwardNotice-{award.AwardNoticeNumber}", ex, award);
                        result.Errors.Add($"AwardNoticeNumber {award.AwardNoticeNumber}: {ex.Message}");
                    }
                }

                // Final save
                if (result.ItemsAdded > 0 || result.ItemsUpdated > 0)
                {
                    await _context.SaveChangesAsync();
                }

                result.EndTime = DateTime.UtcNow;
                result.Success = result.ItemsFailed == 0;
                result.Summary = $"Processed {result.ItemsProcessed}: {result.ItemsAdded} added, {result.ItemsUpdated} updated, {result.ItemsFailed} failed";

                await SaveJobHistoryAsync(result);
                TrackDBProgress($"FINISH DB SYNC: {result.Summary}");
                return result;
            }
            catch (Exception ex)
            {
                LogDetailedError("SyncAwardNoticesAsync", ex);
                TrackDBProgress($"FATAL DB ERROR (Awards): {ex.Message} | Inner: {ex.InnerException?.Message} | InnerInner: {ex.InnerException?.InnerException?.Message}");
                result.Success = false;
                result.Errors.Add(ex.Message);
                result.Errors.Add(ex.InnerException?.Message ?? "No inner exception");
                result.Errors.Add(ex.InnerException?.InnerException?.Message ?? "No inner inner exception");
                return result;
            }
        }

        public async Task<SyncResult> SyncProcurementPlansAsync(List<AnnualProcurementPlanDto> plans)
        {
            var result = new SyncResult { JobType = "ProcurementPlans", StartTime = DateTime.UtcNow };
            TrackDBProgress($"START DB SYNC: Processing {plans.Count} Procurement Plans");

            try
            {
                if (plans.Count == 0)
                {
                    TrackDBProgress("SKIP: No procurement plans received.");
                    result.EndTime = DateTime.UtcNow;
                    result.Success = true;
                    result.Summary = "No procurement plans to process";
                    await SaveJobHistoryAsync(result);
                    return result;
                }

                // Deduplicate by ProcuringEntity + Year - keep the first occurrence
                var originalCount = plans.Count;
                plans = plans
                    .Where(p => !string.IsNullOrEmpty(p.ProcuringEntity) && !string.IsNullOrEmpty(p.Year))
                    .GroupBy(p => new { Entity = SafeTruncate(p.ProcuringEntity, 450), p.Year })
                    .Select(g => g.First())
                    .ToList();

                if (originalCount != plans.Count)
                {
                    var duplicateCount = originalCount - plans.Count;
                    TrackDBProgress($"DEDUP: Removed {duplicateCount} duplicate plans from scraped data");
                    _logger.LogWarning($"Removed {duplicateCount} duplicate procurement plans from batch");
                }

                foreach (var plan in plans)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(plan.ProcuringEntity) || string.IsNullOrEmpty(plan.Year))
                        {
                            result.ItemsFailed++;
                            TrackDBProgress($"SKIP: Empty ProcuringEntity or Year");
                            continue;
                        }

                        // Truncate ProcuringEntity to 450 characters to fit SQL Server index limit
                        var truncatedEntity = SafeTruncate(plan.ProcuringEntity, 450);

                        var existing = await _context.ProcurementPlans
                            .FirstOrDefaultAsync(p => p.ProcuringEntity == truncatedEntity && p.Year == plan.Year);

                        if (existing == null)
                        {
                            var entity = MapToProcurementPlanEntity(plan);
                            entity.ProcuringEntity = truncatedEntity; // Ensure truncation
                            await _context.ProcurementPlans.AddAsync(entity);
                            result.ItemsAdded++;
                        }
                        else
                        {
                            UpdateProcurementPlanEntity(existing, plan);
                            result.ItemsUpdated++;
                        }

                        result.ItemsProcessed++;

                        // Save in batches
                        if (result.ItemsProcessed % 50 == 0)
                        {
                            await _context.SaveChangesAsync();
                            TrackDBProgress($"BATCH SAVE: Saved {result.ItemsProcessed} procurement plans");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ItemsFailed++;
                        LogDetailedError($"SyncProcurementPlan-{plan.ProcuringEntity}-{plan.Year}", ex, plan);
                        result.Errors.Add($"Plan {SafeTruncate(plan.ProcuringEntity, 50)}-{plan.Year}: {ex.Message}");
                    }
                }

                // Final save
                if (result.ItemsAdded > 0 || result.ItemsUpdated > 0)
                {
                    await _context.SaveChangesAsync();
                }

                result.EndTime = DateTime.UtcNow;
                result.Success = result.ItemsFailed == 0;
                result.Summary = $"Processed {result.ItemsProcessed}: {result.ItemsAdded} added, {result.ItemsUpdated} updated, {result.ItemsFailed} failed";

                await SaveJobHistoryAsync(result);
                TrackDBProgress($"FINISH DB SYNC: {result.Summary}");
                return result;
            }
            catch (Exception ex)
            {
                LogDetailedError("SyncProcurementPlansAsync", ex);
                TrackDBProgress($"FATAL DB ERROR (Plans): {ex.Message} | Inner: {ex.InnerException?.Message} | InnerInner: {ex.InnerException?.InnerException?.Message}");
                result.Success = false;
                result.Errors.Add(ex.Message);
                result.Errors.Add(ex.InnerException?.Message ?? "No inner exception");
                result.Errors.Add(ex.InnerException?.InnerException?.Message ?? "No inner inner exception");
                return result;
            }
        }

        public async Task<SyncResult> VerifyAndMoveTendersAsync()
        {
            var result = new SyncResult { JobType = "VerifyAndMove", StartTime = DateTime.UtcNow };
            TrackDBProgress("START DB SYNC: Verifying/Moving Tenders");

            try
            {
                var tendersToVerify = await _context.LiveTenders
                    .Where(t => !t.MovedToClosed &&
                               (t.LastVerifiedAt == null ||
                                t.LastVerifiedAt < DateTime.UtcNow.AddHours(-24)))
                    .ToListAsync();

                TrackDBProgress($"PROGRESS: Verifying {tendersToVerify.Count} live tenders in database...");

                foreach (var tender in tendersToVerify)
                {
                    try
                    {
                        if (tender.ClosingDate.HasValue && tender.ClosingDate < DateTime.UtcNow)
                        {
                            await _tenderRepo.MoveToClosed(tender.TenderId, "System");
                            result.ItemsProcessed++;

                            if (result.ItemsProcessed % 10 == 0)
                            {
                                TrackDBProgress($"PROGRESS: Moved {result.ItemsProcessed} tenders to closed");
                            }
                        }
                        else
                        {
                            tender.LastVerifiedAt = DateTime.UtcNow;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ItemsFailed++;
                        LogDetailedError($"VerifyTender-{tender.TenderId}", ex, tender);
                        result.Errors.Add($"Failed to verify tender {tender.TenderId}: {ex.Message}");
                    }
                }

                await _context.SaveChangesAsync();
                result.EndTime = DateTime.UtcNow;
                result.Success = result.ItemsFailed == 0;
                result.Summary = $"Verified {tendersToVerify.Count}, moved {result.ItemsProcessed} to closed, {result.ItemsFailed} failed";

                await SaveJobHistoryAsync(result);
                TrackDBProgress($"FINISH DB SYNC: {result.Summary}");
                return result;
            }
            catch (Exception ex)
            {
                LogDetailedError("VerifyAndMoveTendersAsync", ex);
                TrackDBProgress($"FATAL DB ERROR (Verify): {ex.Message} | Inner: {ex.InnerException?.Message}");
                result.Success = false;
                result.Errors.Add(ex.Message);
                result.Errors.Add(ex.InnerException?.Message ?? "No inner exception");
                return result;
            }
        }

        public async Task<SyncResult> SyncProcurementPlanItemsAsync(int procurementPlanId, List<ProcurementPlanItem> items)
        {
            var result = new SyncResult { JobType = "ProcurementPlanItems", StartTime = DateTime.UtcNow };
            TrackDBProgress($"START DB SYNC: Processing {items.Count} Procurement Plan Items for plan {procurementPlanId}");

            try
            {
                if (items.Count == 0)
                {
                    result.EndTime = DateTime.UtcNow;
                    result.Success = true;
                    result.Summary = "No items to process";
                    return result;
                }

                foreach (var item in items)
                {
                    try
                    {
                        var existing = await _context.ProcurementPlanItems
                            .FirstOrDefaultAsync(i => i.ProcurementPlanId == procurementPlanId && 
                                                     i.RefNo == item.RefNo);

                        if (existing == null)
                        {
                            var entity = MapToProcurementPlanItemEntity(item, procurementPlanId);
                            await _context.ProcurementPlanItems.AddAsync(entity);
                            result.ItemsAdded++;
                        }
                        else
                        {
                            UpdateProcurementPlanItemEntity(existing, item);
                            result.ItemsUpdated++;
                        }

                        result.ItemsProcessed++;

                        if (result.ItemsProcessed % 100 == 0)
                        {
                            await _context.SaveChangesAsync();
                            TrackDBProgress($"BATCH SAVE: Saved {result.ItemsProcessed} items");
                        }
                    }
                    catch (Exception ex)
                    {
                        result.ItemsFailed++;
                        LogDetailedError($"SyncPlanItem-{item.RefNo}", ex, item);
                        result.Errors.Add($"Item {item.RefNo}: {ex.Message}");
                    }
                }

                if (result.ItemsAdded > 0 || result.ItemsUpdated > 0)
                {
                    await _context.SaveChangesAsync();
                }

                result.EndTime = DateTime.UtcNow;
                result.Success = result.ItemsFailed == 0;
                result.Summary = $"Processed {result.ItemsProcessed}: {result.ItemsAdded} added, {result.ItemsUpdated} updated, {result.ItemsFailed} failed";

                TrackDBProgress($"FINISH DB SYNC: {result.Summary}");
                return result;
            }
            catch (Exception ex)
            {
                LogDetailedError("SyncProcurementPlanItemsAsync", ex);
                result.Success = false;
                result.Errors.Add(ex.Message);
                return result;
            }
        }

        public async Task<SyncResult> SyncProcurementPlanWithItemsAsync(AnnualProcurementPlanDetailDto planDetail)
        {
            var result = new SyncResult { JobType = "ProcurementPlanWithItems", StartTime = DateTime.UtcNow };
            TrackDBProgress($"START DB SYNC: Processing procurement plan with {planDetail.Items.Count} items");

            try
            {
                var truncatedEntity = SafeTruncate(planDetail.ProcuringEntity, 450);

                var existing = await _context.ProcurementPlans
                    .FirstOrDefaultAsync(p => p.ProcuringEntity == truncatedEntity && p.Year == planDetail.Year);

                int planId;
                if (existing == null)
                {
                    var entity = new ProcurementPlanEntity
                    {
                        ProcuringEntity = truncatedEntity,
                        Year = SafeTruncate(planDetail.Year, 10) ?? "",
                        ViewAppUrl = planDetail.SourceUrl,
                        Title = planDetail.Title,
                        TotalItems = planDetail.TotalItems,
                        TotalEstimatedValue = planDetail.TotalEstimatedValue,
                        LastScrapedAt = DateTime.UtcNow,
                        CreatedAt = DateTime.UtcNow,
                        CreatedBy = "System"
                    };
                    await _context.ProcurementPlans.AddAsync(entity);
                    await _context.SaveChangesAsync();
                    planId = entity.Id;
                    result.ItemsAdded++;
                }
                else
                {
                    existing.Title = planDetail.Title;
                    existing.TotalItems = planDetail.TotalItems;
                    existing.TotalEstimatedValue = planDetail.TotalEstimatedValue;
                    existing.LastScrapedAt = DateTime.UtcNow;
                    existing.UpdatedAt = DateTime.UtcNow;
                    existing.UpdatedBy = "System";
                    await _context.SaveChangesAsync();
                    planId = existing.Id;
                    result.ItemsUpdated++;
                }

                // Now sync items
                var itemsResult = await SyncProcurementPlanItemsAsync(planId, planDetail.Items);
                result.ItemsProcessed = itemsResult.ItemsProcessed;
                result.ItemsFailed = itemsResult.ItemsFailed;
                result.Errors.AddRange(itemsResult.Errors);

                result.EndTime = DateTime.UtcNow;
                result.Success = result.ItemsFailed == 0;
                result.Summary = $"Plan: {result.ItemsAdded} added, {result.ItemsUpdated} updated. Items: {itemsResult.ItemsAdded} added, {itemsResult.ItemsUpdated} updated, {result.ItemsFailed} failed";

                TrackDBProgress($"FINISH DB SYNC: {result.Summary}");
                return result;
            }
            catch (Exception ex)
            {
                LogDetailedError("SyncProcurementPlanWithItemsAsync", ex);
                result.Success = false;
                result.Errors.Add(ex.Message);
                return result;
            }
        }

        // --- Helper Mapping Methods with Safe Truncation ---

        private LiveTenderEntity MapToLiveTenderEntity(Tender tender)
        {
            return new LiveTenderEntity
            {
                TenderId = SafeTruncate(tender.TenderId, 50) ?? "",
                ReferenceNumber = SafeTruncate(tender.ReferenceNumber, 200),
                Title = SafeTruncate(tender.Title, 1000) ?? "",
                CategoryCodes = JsonSerializer.Serialize(tender.CategoryCodes ?? new List<string>()),
                CategoryNames = JsonSerializer.Serialize(tender.CategoryNames ?? new List<string>()),
                ProcuringEntity = SafeTruncate(tender.ProcuringEntity, 1000) ?? "",
                Scope = tender.Scope,
                PublishDate = tender.PublishDate,
                ClosingDate = tender.ClosingDate,
                DetailsUrl = tender.DetailsUrl,
                SourceUrl = tender.SourceUrl,
                PageNumber = tender.PageNumber,
                LastScrapedAt = DateTime.UtcNow,
                LastVerifiedAt = DateTime.UtcNow
            };
        }

        private ClosedTenderEntity MapToClosedTenderEntity(Tender tender)
        {
            return new ClosedTenderEntity
            {
                TenderId = SafeTruncate(tender.TenderId, 50) ?? "",
                ReferenceNumber = SafeTruncate(tender.ReferenceNumber, 200),
                Title = tender.Title ?? "",
                CategoryCodes = JsonSerializer.Serialize(tender.CategoryCodes ?? new List<string>()),
                CategoryNames = JsonSerializer.Serialize(tender.CategoryNames ?? new List<string>()),
                ProcuringEntity = tender.ProcuringEntity ?? "",
                Scope = tender.Scope,
                PublishDate = tender.PublishDate,
                ClosingDate = tender.ClosingDate,
                DetailsUrl = tender.DetailsUrl,
                SourceUrl = tender.SourceUrl,
                PageNumber = tender.PageNumber,
                Status = "Closed",
                ActualClosingDate = DateTime.UtcNow,
                LastScrapedAt = DateTime.UtcNow
            };
        }

        private AwardNoticeEntity MapToAwardNoticeEntity(AwardNoticeDto award)
        {
            return new AwardNoticeEntity
            {
                AwardNoticeNumber = SafeTruncate(award.AwardNoticeNumber, 100) ?? "",
                TenderId = SafeTruncate(award.TenderId, 50) ?? "",
                AwardTitle = award.AwardTitle ?? "",
                Awardee = award.Awardee ?? "",
                AwardDate = SafeTruncate(award.AwardDate, 100) ?? "",
                DetailsUrl = award.DetailsUrl ?? "",
                Currency = SafeTruncate(award.Currency, 50) ?? "USD",
                ContractValue = award.ContractValue,
                LastScrapedAt = DateTime.UtcNow
            };
        }

        private ProcurementPlanEntity MapToProcurementPlanEntity(AnnualProcurementPlanDto plan)
        {
            return new ProcurementPlanEntity
            {
                ProcuringEntity = SafeTruncate(plan.ProcuringEntity, 450) ?? "",
                Year = SafeTruncate(plan.Year, 10) ?? "",
                ViewAppUrl = plan.ViewAppUrl,
                LastScrapedAt = DateTime.UtcNow
            };
        }

        private void UpdateClosedTenderEntity(ClosedTenderEntity existing, Tender tender)
        {
            existing.Title = tender.Title ?? existing.Title;
            existing.ReferenceNumber = SafeTruncate(tender.ReferenceNumber, 200);
            existing.CategoryCodes = JsonSerializer.Serialize(tender.CategoryCodes ?? new List<string>());
            existing.CategoryNames = JsonSerializer.Serialize(tender.CategoryNames ?? new List<string>());
            existing.ProcuringEntity = tender.ProcuringEntity ?? existing.ProcuringEntity;
            existing.Scope = tender.Scope;
            existing.PublishDate = tender.PublishDate;
            existing.ClosingDate = tender.ClosingDate;
            existing.DetailsUrl = tender.DetailsUrl;
            existing.SourceUrl = tender.SourceUrl;
            existing.LastScrapedAt = DateTime.UtcNow;
        }

        private void UpdateAwardNoticeEntity(AwardNoticeEntity existing, AwardNoticeDto award)
        {
            existing.TenderId = SafeTruncate(award.TenderId, 50) ?? existing.TenderId;
            existing.AwardTitle = award.AwardTitle ?? existing.AwardTitle;
            existing.Awardee = award.Awardee ?? existing.Awardee;
            existing.AwardDate = SafeTruncate(award.AwardDate, 100) ?? existing.AwardDate;
            existing.DetailsUrl = award.DetailsUrl ?? existing.DetailsUrl;
            existing.Currency = SafeTruncate(award.Currency, 50) ?? "USD";
            existing.ContractValue = award.ContractValue;
            existing.LastScrapedAt = DateTime.UtcNow;
        }

        // ENHANCEMENT: Link award notices to their corresponding tenders
        private async Task LinkAwardToTenderAsync(AwardNoticeEntity award, AwardNoticeDto awardDto)
        {
            try
            {
                if (string.IsNullOrEmpty(award.TenderId))
                {
                    TrackDBProgress($"SKIP LINKING: Award {award.AwardNoticeNumber} has no TenderId");
                    return;
                }

                // Try to find the tender in closed tenders
                var closedTender = await _context.ClosedTenders
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TenderId == award.TenderId || t.ReferenceNumber == award.TenderId);

                if (closedTender != null)
                {
                    // Link the award to the tender
                    if (award.ClosedTenders == null)
                        award.ClosedTenders = new List<ClosedTenderEntity>();

                    // Only add if not already linked
                    if (!award.ClosedTenders.Any(t => t.Id == closedTender.Id))
                    {
                        var tenderToLink = await _context.ClosedTenders
                            .FirstOrDefaultAsync(t => t.Id == closedTender.Id);
                        
                        if (tenderToLink != null)
                        {
                            award.ClosedTenders.Add(tenderToLink);
                            TrackDBProgress($"LINKED: Award {award.AwardNoticeNumber} to Tender {award.TenderId}");
                        }
                    }
                }
                else
                {
                    TrackDBProgress($"NO MATCH: Award {award.AwardNoticeNumber} tender {award.TenderId} not found in closed tenders");
                }
            }
            catch (Exception ex)
            {
                LogDetailedError($"LinkAwardToTender-{award.AwardNoticeNumber}", ex);
                // Don't fail the whole sync if linking fails
            }
        }

        private void UpdateProcurementPlanEntity(ProcurementPlanEntity existing, AnnualProcurementPlanDto plan)
        {
            existing.ViewAppUrl = plan.ViewAppUrl ?? existing.ViewAppUrl;
            existing.LastScrapedAt = DateTime.UtcNow;
        }

        private ProcurementPlanItemEntity MapToProcurementPlanItemEntity(ProcurementPlanItem item, int procurementPlanId)
        {
            return new ProcurementPlanItemEntity
            {
                ProcurementPlanId = procurementPlanId,
                ItemId = SafeTruncate(item.ItemId, 100) ?? "",
                RefNo = SafeTruncate(item.RefNo, 100) ?? "",
                ClassOfProcurement = SafeTruncate(item.ClassOfProcurement, 200) ?? "",
                ObjectCode = SafeTruncate(item.ObjectCode, 100) ?? "",
                Description = item.Description ?? "",
                PmoEndUser = SafeTruncate(item.PmoEndUser, 200) ?? "",
                ProcurementMethod = SafeTruncate(item.ProcurementMethod, 100) ?? "",
                EoiPublicationDate = SafeTruncate(item.EoiPublicationDate, 50) ?? "",
                EoiClosingDate = SafeTruncate(item.EoiClosingDate, 50) ?? "",
                TenderPublicationDate = SafeTruncate(item.TenderPublicationDate, 50) ?? "",
                BidClosingDate = SafeTruncate(item.BidClosingDate, 50) ?? "",
                AwardNoticeDate = SafeTruncate(item.AwardNoticeDate, 50) ?? "",
                ContractSigningDate = SafeTruncate(item.ContractSigningDate, 50) ?? "",
                CycleDays = SafeTruncate(item.CycleDays, 50) ?? "",
                LeadTime = SafeTruncate(item.LeadTime, 50) ?? "",
                Spoc = SafeTruncate(item.Spoc, 200) ?? "",
                SourceOfFunds = SafeTruncate(item.SourceOfFunds, 200) ?? "",
                UnitOfMeasurement = SafeTruncate(item.UnitOfMeasurement, 50) ?? "",
                Quantity = SafeTruncate(item.Quantity, 50) ?? "",
                Comments = item.Comments ?? "",
                IsSupplement = item.IsSupplement,
                ParsedTenderPublicationDate = item.ParsedTenderPublicationDate,
                ParsedBidClosingDate = item.ParsedBidClosingDate,
                ParsedAwardNoticeDate = item.ParsedAwardNoticeDate,
                ParsedContractSigningDate = item.ParsedContractSigningDate,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "System"
            };
        }

        private void UpdateProcurementPlanItemEntity(ProcurementPlanItemEntity existing, ProcurementPlanItem item)
        {
            existing.ItemId = SafeTruncate(item.ItemId, 100) ?? existing.ItemId;
            existing.ClassOfProcurement = SafeTruncate(item.ClassOfProcurement, 200) ?? existing.ClassOfProcurement;
            existing.ObjectCode = SafeTruncate(item.ObjectCode, 100) ?? existing.ObjectCode;
            existing.Description = item.Description ?? existing.Description;
            existing.PmoEndUser = SafeTruncate(item.PmoEndUser, 200) ?? existing.PmoEndUser;
            existing.ProcurementMethod = SafeTruncate(item.ProcurementMethod, 100) ?? existing.ProcurementMethod;
            existing.EoiPublicationDate = SafeTruncate(item.EoiPublicationDate, 50) ?? existing.EoiPublicationDate;
            existing.EoiClosingDate = SafeTruncate(item.EoiClosingDate, 50) ?? existing.EoiClosingDate;
            existing.TenderPublicationDate = SafeTruncate(item.TenderPublicationDate, 50) ?? existing.TenderPublicationDate;
            existing.BidClosingDate = SafeTruncate(item.BidClosingDate, 50) ?? existing.BidClosingDate;
            existing.AwardNoticeDate = SafeTruncate(item.AwardNoticeDate, 50) ?? existing.AwardNoticeDate;
            existing.ContractSigningDate = SafeTruncate(item.ContractSigningDate, 50) ?? existing.ContractSigningDate;
            existing.CycleDays = SafeTruncate(item.CycleDays, 50) ?? existing.CycleDays;
            existing.LeadTime = SafeTruncate(item.LeadTime, 50) ?? existing.LeadTime;
            existing.Spoc = SafeTruncate(item.Spoc, 200) ?? existing.Spoc;
            existing.SourceOfFunds = SafeTruncate(item.SourceOfFunds, 200) ?? existing.SourceOfFunds;
            existing.UnitOfMeasurement = SafeTruncate(item.UnitOfMeasurement, 50) ?? existing.UnitOfMeasurement;
            existing.Quantity = SafeTruncate(item.Quantity, 50) ?? existing.Quantity;
            existing.Comments = item.Comments ?? existing.Comments;
            existing.IsSupplement = item.IsSupplement;
            existing.ParsedTenderPublicationDate = item.ParsedTenderPublicationDate;
            existing.ParsedBidClosingDate = item.ParsedBidClosingDate;
            existing.ParsedAwardNoticeDate = item.ParsedAwardNoticeDate;
            existing.ParsedContractSigningDate = item.ParsedContractSigningDate;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = "System";
        }

        private async Task SaveJobHistoryAsync(SyncResult result)
        {
            try
            {
                var jobHistory = new ScrapingJobHistoryEntity
                {
                    JobType = result.JobType,
                    StartTime = result.StartTime,
                    EndTime = result.EndTime ?? DateTime.UtcNow,
                    Status = result.Success ? "Completed" : "Failed",
                    ItemsProcessed = result.ItemsProcessed,
                    ItemsAdded = result.ItemsAdded,
                    ItemsUpdated = result.ItemsUpdated,
                    ItemsFailed = result.ItemsFailed,
                    ErrorDetails = (result.Errors?.Any() == true) ? JsonSerializer.Serialize(result.Errors) : null,
                    Summary = result.Summary,
                    TriggeredBy = "System"
                };

                await _context.ScrapingJobHistory.AddAsync(jobHistory);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                LogDetailedError("SaveJobHistory", ex, result);
                _logger.LogError(ex, $"Error saving job history for {result.JobType}");
            }
        }
    }

    public class SyncResult
    {
        public string JobType { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public bool Success { get; set; }
        public int ItemsProcessed { get; set; }
        public int ItemsAdded { get; set; }
        public int ItemsUpdated { get; set; }
        public int ItemsFailed { get; set; }
        public List<string> Errors { get; set; } = new List<string>();
        public string Summary { get; set; }
    }
}