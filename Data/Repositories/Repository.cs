// ZimbabweTenderAPI.Data.Repositories (No functional changes)
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;

namespace ZimbabweTenderAPI.Data.Repositories
{
    // Generic Repository Interface
    public interface IRepository<T> where T : AuditableEntity
    {
        Task<T> GetByIdAsync(int id, bool includeDeleted = false);
        Task<IEnumerable<T>> GetAllAsync(bool includeDeleted = false);
        Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, bool includeDeleted = false);
        Task<T> AddAsync(T entity);
        Task<T> UpdateAsync(T entity);
        Task<bool> DeleteAsync(int id, bool hardDelete = false);
        Task<bool> RestoreAsync(int id);
        Task<int> CountAsync(Expression<Func<T, bool>> predicate = null, bool includeDeleted = false);
        Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, bool includeDeleted = false);
    }

    // Generic Repository Implementation
    public class Repository<T> : IRepository<T> where T : AuditableEntity
    {
        protected readonly ApplicationDbContext _context;
        protected readonly DbSet<T> _dbSet;
        protected readonly ILogger<Repository<T>> _logger;

        public Repository(ApplicationDbContext context, ILogger<Repository<T>> logger)
        {
            _context = context;
            _dbSet = context.Set<T>();
            _logger = logger;
        }

        public virtual async Task<T> GetByIdAsync(int id, bool includeDeleted = false)
        {
            try
            {
                var query = includeDeleted
                    ? _dbSet.IgnoreQueryFilters()
                    : _dbSet;

                return await query.FirstOrDefaultAsync(e => e.Id == id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting {typeof(T).Name} by ID {id}");
                throw;
            }
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync(bool includeDeleted = false)
        {
            try
            {
                var query = includeDeleted
                    ? _dbSet.IgnoreQueryFilters()
                    : _dbSet;

                return await query.ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting all {typeof(T).Name}");
                throw;
            }
        }

        public virtual async Task<IEnumerable<T>> FindAsync(
            Expression<Func<T, bool>> predicate,
            bool includeDeleted = false)
        {
            try
            {
                var query = includeDeleted
                    ? _dbSet.IgnoreQueryFilters()
                    : _dbSet;

                return await query.Where(predicate).ToListAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error finding {typeof(T).Name}");
                throw;
            }
        }

        public virtual async Task<T> AddAsync(T entity)
        {
            try
            {
                await _dbSet.AddAsync(entity);
                await _context.SaveChangesAsync();
                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error adding {typeof(T).Name}");
                throw;
            }
        }

        public virtual async Task<T> UpdateAsync(T entity)
        {
            try
            {
                _dbSet.Update(entity);
                await _context.SaveChangesAsync();
                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating {typeof(T).Name}");
                throw;
            }
        }

        public virtual async Task<bool> DeleteAsync(int id, bool hardDelete = false)
        {
            try
            {
                var entity = await GetByIdAsync(id);
                if (entity == null)
                    return false;

                if (hardDelete)
                {
                    _dbSet.Remove(entity);
                }
                else
                {
                    entity.IsDeleted = true;
                    entity.DeletedAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting {typeof(T).Name} with ID {id}");
                throw;
            }
        }

        public virtual async Task<bool> RestoreAsync(int id)
        {
            try
            {
                var entity = await GetByIdAsync(id, includeDeleted: true);
                if (entity == null || !entity.IsDeleted)
                    return false;

                entity.IsDeleted = false;
                entity.DeletedAt = null;
                entity.DeletedBy = null;

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error restoring {typeof(T).Name} with ID {id}");
                throw;
            }
        }

        public virtual async Task<int> CountAsync(
            Expression<Func<T, bool>> predicate = null,
            bool includeDeleted = false)
        {
            try
            {
                var query = includeDeleted
                    ? _dbSet.IgnoreQueryFilters()
                    : _dbSet;

                return predicate == null
                    ? await query.CountAsync()
                    : await query.CountAsync(predicate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error counting {typeof(T).Name}");
                throw;
            }
        }

        public virtual async Task<bool> ExistsAsync(
            Expression<Func<T, bool>> predicate,
            bool includeDeleted = false)
        {
            try
            {
                var query = includeDeleted
                    ? _dbSet.IgnoreQueryFilters()
                    : _dbSet;

                return await query.AnyAsync(predicate);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking existence in {typeof(T).Name}");
                throw;
            }
        }
    }

    // Tender-specific Repository Interface
    public interface ITenderRepository : IRepository<LiveTenderEntity>
    {
        Task<LiveTenderEntity> GetByTenderIdAsync(string tenderId);
        Task<IEnumerable<LiveTenderEntity>> GetByProcuringEntityAsync(string entity);
        Task<IEnumerable<LiveTenderEntity>> GetClosingSoonAsync(int days);
        Task<IEnumerable<LiveTenderEntity>> SearchAsync(string searchTerm);
        Task<LiveTenderEntity> UpsertAsync(LiveTenderEntity tender, string changedBy);
        Task<bool> MoveToClosed(string tenderId, string movedBy);
    }

    // Tender Repository Implementation
    public class TenderRepository : Repository<LiveTenderEntity>, ITenderRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TenderRepository> _logger;

        public TenderRepository(
            ApplicationDbContext context,
            ILogger<TenderRepository> logger) : base(context, logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<LiveTenderEntity> GetByTenderIdAsync(string tenderId)
        {
            return await _dbSet
                .Include(t => t.AuditLogs)
                .FirstOrDefaultAsync(t => t.TenderId == tenderId);
        }

        public async Task<IEnumerable<LiveTenderEntity>> GetByProcuringEntityAsync(string entity)
        {
            return await _dbSet
                .Where(t => t.ProcuringEntity.Contains(entity))
                .OrderByDescending(t => t.PublishDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<LiveTenderEntity>> GetClosingSoonAsync(int days)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(days);
            return await _dbSet
                .Where(t => t.ClosingDate.HasValue &&
                            t.ClosingDate >= DateTime.UtcNow &&
                            t.ClosingDate <= cutoffDate)
                .OrderBy(t => t.ClosingDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<LiveTenderEntity>> SearchAsync(string searchTerm)
        {
            return await _dbSet
                .Where(t => t.Title.Contains(searchTerm) ||
                            t.ProcuringEntity.Contains(searchTerm) ||
                            t.Scope.Contains(searchTerm) ||
                            t.ReferenceNumber.Contains(searchTerm))
                .OrderByDescending(t => t.PublishDate)
                .ToListAsync();
        }

        public async Task<LiveTenderEntity> UpsertAsync(LiveTenderEntity tender, string changedBy)
        {
            var strategy = _context.Database.CreateExecutionStrategy();

            LiveTenderEntity resultEntity = tender; // To hold the final entity

            // FIX: Wrap transaction logic with ExecuteAsync for retrying execution strategy
            await strategy.ExecuteAsync(async () =>
            {
                // The explicit transaction MUST be inside the ExecuteAsync block
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    // Query for existing record within transaction scope using TenderId (unique index)
                    var existing = await _dbSet.FirstOrDefaultAsync(t => t.TenderId == tender.TenderId);

                    if (existing == null)
                    {
                        // Add new tender
                        tender.CreatedBy = changedBy;
                        tender.CreatedAt = DateTime.UtcNow;
                        await _dbSet.AddAsync(tender);

                        // Add audit log
                        // Passing null for OldValues and Changes is now safe due to nullability fixes
                        await AddAuditLogAsync(tender.TenderId, "Created", null, tender, changedBy);
                    }
                    else
                    {
                        // Check what changed
                        var changes = CompareChanges(existing, tender);

                        if (changes.Any())
                        {
                            // Update existing tender
                            var oldValues = CloneEntity(existing);

                            existing.Title = tender.Title;
                            existing.ReferenceNumber = tender.ReferenceNumber;
                            existing.CategoryCodes = tender.CategoryCodes;
                            existing.CategoryNames = tender.CategoryNames;
                            existing.ProcuringEntity = tender.ProcuringEntity;
                            existing.Scope = tender.Scope;
                            existing.PublishDate = tender.PublishDate;
                            existing.ClosingDate = tender.ClosingDate;
                            existing.DetailsUrl = tender.DetailsUrl;
                            existing.SourceUrl = tender.SourceUrl;
                            existing.PageNumber = tender.PageNumber;
                            existing.LastScrapedAt = DateTime.UtcNow;
                            existing.LastVerifiedAt = DateTime.UtcNow;
                            existing.UpdatedBy = changedBy;
                            existing.UpdatedAt = DateTime.UtcNow;

                            // Add audit log
                            await AddAuditLogAsync(tender.TenderId, "Updated", oldValues, existing, changedBy, changes);
                            resultEntity = existing;
                        }
                        else
                        {
                            // No changes, just update verification timestamp
                            existing.LastVerifiedAt = DateTime.UtcNow;
                            resultEntity = existing;
                        }
                    }

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, $"Transaction rolled back during upsert of tender {tender.TenderId}");
                    throw; // Re-throw for ExecuteAsync to handle retry or final error
                }
            });

            return resultEntity;
        }

        public async Task<bool> MoveToClosed(string tenderId, string movedBy)
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            bool success = false;

            // FIX: Wrap transaction logic with ExecuteAsync for retrying execution strategy
            await strategy.ExecuteAsync(async () =>
            {
                // The explicit transaction MUST be inside the ExecuteAsync block
                using var transaction = await _context.Database.BeginTransactionAsync();
                try
                {
                    var liveTender = await GetByTenderIdAsync(tenderId);
                    if (liveTender == null)
                    {
                        success = false;
                        return;
                    }

                    // Create closed tender record
                    var closedTender = new ClosedTenderEntity
                    {
                        TenderId = liveTender.TenderId,
                        ReferenceNumber = liveTender.ReferenceNumber,
                        Title = liveTender.Title,
                        CategoryCodes = liveTender.CategoryCodes,
                        CategoryNames = liveTender.CategoryNames,
                        ProcuringEntity = liveTender.ProcuringEntity,
                        Scope = liveTender.Scope,
                        PublishDate = liveTender.PublishDate,
                        ClosingDate = liveTender.ClosingDate,
                        DetailsUrl = liveTender.DetailsUrl,
                        SourceUrl = liveTender.SourceUrl,
                        PageNumber = liveTender.PageNumber,
                        Status = "Closed",
                        ActualClosingDate = DateTime.UtcNow,
                        LastScrapedAt = DateTime.UtcNow,
                        CreatedBy = movedBy
                    };

                    await _context.ClosedTenders.AddAsync(closedTender);

                    // Mark live tender as moved
                    liveTender.MovedToClosed = true;
                    liveTender.MovedToClosedAt = DateTime.UtcNow;

                    // Add audit log
                    // Passing null for NewValues and Changes is safe. OldValues will be populated.
                    await AddAuditLogAsync(tenderId, "MovedToClosed", liveTender, null, movedBy);

                    // Soft delete from live tenders
                    liveTender.IsDeleted = true;
                    liveTender.DeletedAt = DateTime.UtcNow;
                    liveTender.DeletedBy = movedBy;

                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();
                    success = true;
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    _logger.LogError(ex, $"Transaction rolled back during move of tender {tenderId}");
                    throw; // Re-throw for ExecuteAsync to handle retry or final error
                }
            });
            return success;
        }

        private async Task AddAuditLogAsync(
            string tenderId,
            string action,
            LiveTenderEntity oldValues,
            LiveTenderEntity newValues,
            string changedBy,
            Dictionary<string, (object? Old, object? New)> changes = null)
        {
            var auditLog = new TenderAuditLog
            {
                TenderId = tenderId,
                Action = action,
                ChangedBy = changedBy,
                ChangeDate = DateTime.UtcNow,
                // These fields are now explicitly set to null if the source object/dictionary is null,
                // which is now safe because the entity properties are nullable.
                OldValues = oldValues != null ? JsonSerializer.Serialize(oldValues) : null,
                NewValues = newValues != null ? JsonSerializer.Serialize(newValues) : null,
                Changes = changes != null ? JsonSerializer.Serialize(changes) : null,

                // IPAddress and UserAgent are intentionally left as null (default string?) 
                // as this is a background 'System' job.
            };

            await _context.TenderAuditLogs.AddAsync(auditLog);
            // NOTE: No SaveChangesAsync here. It relies on the caller (Upsert/MoveToClosed) to commit the transaction.
        }

        private Dictionary<string, (object? Old, object? New)> CompareChanges(
            LiveTenderEntity existing,
            LiveTenderEntity updated)
        {
            var changes = new Dictionary<string, (object? Old, object? New)>();

            if (existing.Title != updated.Title)
                changes["Title"] = (existing.Title, updated.Title);

            if (existing.ReferenceNumber != updated.ReferenceNumber)
                changes["ReferenceNumber"] = (existing.ReferenceNumber, updated.ReferenceNumber);

            if (existing.ProcuringEntity != updated.ProcuringEntity)
                changes["ProcuringEntity"] = (existing.ProcuringEntity, updated.ProcuringEntity);

            if (existing.Scope != updated.Scope)
                changes["Scope"] = (existing.Scope, updated.Scope);

            if (existing.PublishDate != updated.PublishDate)
                changes["PublishDate"] = (existing.PublishDate, updated.PublishDate);

            if (existing.ClosingDate != updated.ClosingDate)
                changes["ClosingDate"] = (existing.ClosingDate, updated.ClosingDate);

            return changes;
        }

        private LiveTenderEntity CloneEntity(LiveTenderEntity entity)
        {
            return new LiveTenderEntity
            {
                TenderId = entity.TenderId,
                ReferenceNumber = entity.ReferenceNumber,
                Title = entity.Title,
                CategoryCodes = entity.CategoryCodes,
                CategoryNames = entity.CategoryNames,
                ProcuringEntity = entity.ProcuringEntity,
                Scope = entity.Scope,
                PublishDate = entity.PublishDate,
                ClosingDate = entity.ClosingDate,
                DetailsUrl = entity.DetailsUrl,
                SourceUrl = entity.SourceUrl,
                PageNumber = entity.PageNumber
            };
        }
    }
}