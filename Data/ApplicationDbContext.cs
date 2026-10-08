using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Data.Entities;

namespace ZimbabweTenderAPI.Data
{
    public class ApplicationDbContext : DbContext
    {
        private readonly IHttpContextAccessor? _httpContextAccessor;

        public ApplicationDbContext(
             DbContextOptions<ApplicationDbContext> options,
             IHttpContextAccessor? httpContextAccessor = null) : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        // DbSets
        public DbSet<LiveTenderEntity> LiveTenders { get; set; }
        public DbSet<ClosedTenderEntity> ClosedTenders { get; set; }
        public DbSet<AwardNoticeEntity> AwardNotices { get; set; }
        public DbSet<ProcurementPlanEntity> ProcurementPlans { get; set; }
        public DbSet<ProcurementPlanItemEntity> ProcurementPlanItems { get; set; }
        public DbSet<UserEntity> Users { get; set; }
        public DbSet<RoleEntity> Roles { get; set; }
        public DbSet<UserRoleEntity> UserRoles { get; set; }
        public DbSet<TenderAuditLog> TenderAuditLogs { get; set; }
        public DbSet<AwardNoticeAuditLog> AwardNoticeAuditLogs { get; set; }
        public DbSet<ProcurementPlanAuditLog> ProcurementPlanAuditLogs { get; set; }
        public DbSet<UserAuditLog> UserAuditLogs { get; set; }
        public DbSet<SystemConfigurationEntity> SystemConfigurations { get; set; }
        public DbSet<SystemSettingEntity> SystemSettings { get; set; }
        public DbSet<ScrapingJobHistoryEntity> ScrapingJobHistory { get; set; }
        
        // New DbSets for tender workflow
        public DbSet<TenderAssignmentEntity> TenderAssignments { get; set; }
        public DbSet<TenderDocumentEntity> TenderDocuments { get; set; }
        public DbSet<TenderChecklistItemEntity> TenderChecklistItems { get; set; }
        public DbSet<TenderApprovalEntity> TenderApprovals { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            if (Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            {
                foreach (var entityType in modelBuilder.Model.GetEntityTypes())
                {
                    foreach (var property in entityType.GetProperties())
                    {
                        if (property.ClrType == typeof(string))
                        {
                            property.SetColumnType("TEXT");
                        }
                        else if (property.ClrType == typeof(decimal) || property.ClrType == typeof(decimal?))
                        {
                            property.SetColumnType("NUMERIC");
                        }
                    }
                }
            }

            // Configure relationships and constraints

            // LiveTender - TenderAuditLog (Make navigation optional)
            modelBuilder.Entity<LiveTenderEntity>()
                .HasMany(t => t.AuditLogs)
                .WithOne(a => a.LiveTender)
                .HasForeignKey(a => a.LiveTenderId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // ClosedTender - TenderAuditLog (Make navigation optional)
            modelBuilder.Entity<ClosedTenderEntity>()
                .HasMany(t => t.AuditLogs)
                .WithOne(a => a.ClosedTender)
                .HasForeignKey(a => a.ClosedTenderId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // ClosedTender - AwardNotice
            modelBuilder.Entity<ClosedTenderEntity>()
                .HasOne(t => t.AwardNotice)
                .WithMany(a => a.ClosedTenders)
                .HasForeignKey(t => t.AwardNoticeId)
                .OnDelete(DeleteBehavior.Restrict);

            // AwardNotice - AwardNoticeAuditLog (Make navigation optional)
            modelBuilder.Entity<AwardNoticeEntity>()
                .HasMany(a => a.AuditLogs)
                .WithOne(l => l.AwardNotice)
                .HasForeignKey(l => l.AwardNoticeId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // ProcurementPlan - ProcurementPlanItem (Make navigation optional for soft delete compatibility)
            modelBuilder.Entity<ProcurementPlanEntity>()
                .HasMany(p => p.Items)
                .WithOne(i => i.ProcurementPlan)
                .HasForeignKey(i => i.ProcurementPlanId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            // ProcurementPlan - ProcurementPlanAuditLog (Make navigation optional)
            modelBuilder.Entity<ProcurementPlanEntity>()
                .HasMany(p => p.AuditLogs)
                .WithOne(l => l.ProcurementPlan)
                .HasForeignKey(l => l.ProcurementPlanId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // User - UserAuditLog (Make navigation optional)
            modelBuilder.Entity<UserEntity>()
                .HasMany(u => u.AuditLogs)
                .WithOne(l => l.User)
                .HasForeignKey(l => l.UserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Restrict);

            // User - UserRole - Role (Many-to-Many)
            modelBuilder.Entity<UserRoleEntity>()
                .HasOne(ur => ur.User)
                .WithMany(u => u.UserRoles)
                .HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserRoleEntity>()
                .HasOne(ur => ur.Role)
                .WithMany(r => r.UserRoles)
                .HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            // TenderAssignment relationships
            modelBuilder.Entity<TenderAssignmentEntity>()
                .HasOne(ta => ta.AssignedToUser)
                .WithMany()
                .HasForeignKey(ta => ta.AssignedToUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TenderAssignmentEntity>()
                .HasOne(ta => ta.AssignedByUser)
                .WithMany()
                .HasForeignKey(ta => ta.AssignedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // TenderDocument relationships
            modelBuilder.Entity<TenderDocumentEntity>()
                .HasOne(td => td.UploadedByUser)
                .WithMany()
                .HasForeignKey(td => td.UploadedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // TenderChecklistItem relationships
            modelBuilder.Entity<TenderChecklistItemEntity>()
                .HasOne(tc => tc.CompletedByUser)
                .WithMany()
                .HasForeignKey(tc => tc.CompletedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // TenderApproval relationships
            modelBuilder.Entity<TenderApprovalEntity>()
                .HasOne(ta => ta.ApproverUser)
                .WithMany()
                .HasForeignKey(ta => ta.ApproverUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TenderApprovalEntity>()
                .HasOne(ta => ta.RequestedByUser)
                .WithMany()
                .HasForeignKey(ta => ta.RequestedByUserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<TenderApprovalEntity>()
                .HasOne(ta => ta.PreviousApproval)
                .WithMany()
                .HasForeignKey(ta => ta.PreviousApprovalId)
                .OnDelete(DeleteBehavior.Restrict);

            // Query filters for soft delete
            modelBuilder.Entity<LiveTenderEntity>()
                .HasQueryFilter(t => !t.IsDeleted);

            modelBuilder.Entity<ClosedTenderEntity>()
                .HasQueryFilter(t => !t.IsDeleted);

            modelBuilder.Entity<AwardNoticeEntity>()
                .HasQueryFilter(a => !a.IsDeleted);

            modelBuilder.Entity<ProcurementPlanEntity>()
                .HasQueryFilter(p => !p.IsDeleted);

            modelBuilder.Entity<UserEntity>()
                .HasQueryFilter(u => !u.IsDeleted);

            modelBuilder.Entity<TenderAssignmentEntity>()
                .HasQueryFilter(ta => !ta.IsDeleted);

            modelBuilder.Entity<TenderDocumentEntity>()
                .HasQueryFilter(td => !td.IsDeleted);

            modelBuilder.Entity<TenderChecklistItemEntity>()
                .HasQueryFilter(tc => !tc.IsDeleted);

            modelBuilder.Entity<TenderApprovalEntity>()
                .HasQueryFilter(ta => !ta.IsDeleted);

            // Seed initial data
            SeedData(modelBuilder);
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Get current user
            var currentUser = GetCurrentUser();

            // Get all added/modified/deleted entities
            var entries = ChangeTracker.Entries()
                .Where(e => e.Entity is AuditableEntity &&
                           (e.State == EntityState.Added ||
                            e.State == EntityState.Modified ||
                            e.State == EntityState.Deleted));

            foreach (var entry in entries)
            {
                var entity = (AuditableEntity)entry.Entity;

                switch (entry.State)
                {
                    case EntityState.Added:
                        entity.CreatedAt = DateTime.UtcNow;
                        entity.CreatedBy = currentUser;
                        break;

                    case EntityState.Modified:
                        entity.UpdatedAt = DateTime.UtcNow;
                        entity.UpdatedBy = currentUser;
                        break;

                    case EntityState.Deleted:
                        // Soft delete
                        entry.State = EntityState.Modified;
                        entity.IsDeleted = true;
                        entity.DeletedAt = DateTime.UtcNow;
                        entity.DeletedBy = currentUser;
                        break;
                }
            }

            return await base.SaveChangesAsync(cancellationToken);
        }

        private string GetCurrentUser()
        {
            try
            {
                var username = _httpContextAccessor?.HttpContext?.User?.Identity?.Name;
                return string.IsNullOrEmpty(username) ? "System" : username;
            }
            catch
            {
                return "System";
            }
        }

        private void SeedData(ModelBuilder modelBuilder)
        {
            // Seed default roles
            modelBuilder.Entity<RoleEntity>().HasData(
                new RoleEntity
                {
                    Id = 1,
                    Name = "Admin",
                    Description = "Full system access and administrative privileges",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false
                },
                new RoleEntity
                {
                    Id = 2,
                    Name = "Guest",
                    Description = "Read-only access to public data",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false
                },
                new RoleEntity
                {
                    Id = 3,
                    Name = "Supervisor",
                    Description = "Supervisory access with approval permissions",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false
                },
                new RoleEntity
                {
                    Id = 4,
                    Name = "HR",
                    Description = "Human Resources access",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false
                },
                new RoleEntity
                {
                    Id = 5,
                    Name = "ProjectManager",
                    Description = "Project management access",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false
                },
                new RoleEntity
                {
                    Id = 6,
                    Name = "Employee",
                    Description = "Standard employee access",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false
                },
                new RoleEntity
                {
                    Id = 7,
                    Name = "Development",
                    Description = "Development team access",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false
                }
            );

            // Administrative identities are provisioned through the authenticated
            // administration workflow. Never seed a shared default password.

            // Assign all roles to default admin user (full access to everything)
            modelBuilder.Entity<UserRoleEntity>().HasData(
                new UserRoleEntity
                {
                    Id = 1,
                    UserId = 1,
                    RoleId = 1, // Admin role
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = "System"
                },
                new UserRoleEntity
                {
                    Id = 2,
                    UserId = 1,
                    RoleId = 2, // Guest role
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = "System"
                },
                new UserRoleEntity
                {
                    Id = 3,
                    UserId = 1,
                    RoleId = 3, // Supervisor role
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = "System"
                },
                new UserRoleEntity
                {
                    Id = 4,
                    UserId = 1,
                    RoleId = 4, // HR role
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = "System"
                },
                new UserRoleEntity
                {
                    Id = 5,
                    UserId = 1,
                    RoleId = 5, // ProjectManager role
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = "System"
                },
                new UserRoleEntity
                {
                    Id = 6,
                    UserId = 1,
                    RoleId = 6, // Employee role
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = "System"
                },
                new UserRoleEntity
                {
                    Id = 7,
                    UserId = 1,
                    RoleId = 7, // Development role
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = "System"
                }
            );

            // Seed default system configurations
            modelBuilder.Entity<SystemConfigurationEntity>().HasData(
                new SystemConfigurationEntity
                {
                    Id = 1,
                    Key = "ScrapingEnabled",
                    Value = "true",
                    Description = "Enable/disable automatic scraping",
                    Category = "Scraping",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false,
                    UpdatedBy = null,
                    UpdatedAt = null,
                    DeletedBy = null,
                    DeletedAt = null
                },
                new SystemConfigurationEntity
                {
                    Id = 2,
                    Key = "ScrapingIntervalMinutes",
                    Value = "60",
                    Description = "Interval between automatic scraping jobs in minutes",
                    Category = "Scraping",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false,
                    UpdatedBy = null,
                    UpdatedAt = null,
                    DeletedBy = null,
                    DeletedAt = null
                },
                new SystemConfigurationEntity
                {
                    Id = 3,
                    Key = "MaxConcurrentScrapes",
                    Value = "3",
                    Description = "Maximum number of concurrent scraping operations",
                    Category = "Scraping",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false,
                    UpdatedBy = null,
                    UpdatedAt = null,
                    DeletedBy = null,
                    DeletedAt = null
                },
                new SystemConfigurationEntity
                {
                    Id = 4,
                    Key = "RetentionDaysClosedTenders",
                    Value = "365",
                    Description = "Number of days to retain closed tenders in database",
                    Category = "Data",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false,
                    UpdatedBy = null,
                    UpdatedAt = null,
                    DeletedBy = null,
                    DeletedAt = null
                },
                new SystemConfigurationEntity
                {
                    Id = 5,
                    Key = "EmailNotificationsEnabled",
                    Value = "false",
                    Description = "Enable/disable email notifications",
                    Category = "Email",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = "System",
                    IsDeleted = false,
                    UpdatedBy = null,
                    UpdatedAt = null,
                    DeletedBy = null,
                    DeletedAt = null
                }
            );

            // Seed SystemSettings with default configuration values
            modelBuilder.Entity<SystemSettingEntity>().HasData(
                // Background Jobs - Live Tenders
                new SystemSettingEntity { Id = 1, Key = "BackgroundJobs:LiveTendersSync:Enabled", Value = "true", Description = "Enable/disable live tenders sync job", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 2, Key = "BackgroundJobs:LiveTendersSync:IntervalHours", Value = "0.0833", Description = "Live tenders sync interval in hours (0.0833 = 5 minutes)", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                
                // Background Jobs - Closed Tenders
                new SystemSettingEntity { Id = 3, Key = "BackgroundJobs:ClosedTendersSync:Enabled", Value = "true", Description = "Enable/disable closed tenders sync job", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 4, Key = "BackgroundJobs:ClosedTendersSync:IntervalHours", Value = "24", Description = "Closed tenders sync interval in hours", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 5, Key = "BackgroundJobs:ClosedTendersSync:RunAtHour", Value = "3", Description = "Hour to run closed tenders sync (24-hour format)", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                
                // Background Jobs - Award Notices
                new SystemSettingEntity { Id = 6, Key = "BackgroundJobs:AwardNoticesSync:Enabled", Value = "true", Description = "Enable/disable award notices sync job", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 7, Key = "BackgroundJobs:AwardNoticesSync:IntervalHours", Value = "24", Description = "Award notices sync interval in hours", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 8, Key = "BackgroundJobs:AwardNoticesSync:RunAtHour", Value = "2", Description = "Hour to run award notices sync (24-hour format)", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                
                // Background Jobs - Procurement Plans
                new SystemSettingEntity { Id = 9, Key = "BackgroundJobs:ProcurementPlansSync:Enabled", Value = "true", Description = "Enable/disable procurement plans sync job", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 10, Key = "BackgroundJobs:ProcurementPlansSync:IntervalHours", Value = "168", Description = "Procurement plans sync interval in hours (168 = weekly)", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 11, Key = "BackgroundJobs:ProcurementPlansSync:RunAtHour", Value = "4", Description = "Hour to run procurement plans sync (24-hour format)", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                
                // Background Jobs - Verify and Move
                new SystemSettingEntity { Id = 12, Key = "BackgroundJobs:VerifyAndMoveTenders:Enabled", Value = "true", Description = "Enable/disable verify and move tenders job", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 13, Key = "BackgroundJobs:VerifyAndMoveTenders:IntervalHours", Value = "6", Description = "Verify and move interval in hours", Category = "BackgroundJobs", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                
                // JWT Settings
                new SystemSettingEntity { Id = 14, Key = "JWT:ExpirationMinutes", Value = "1440", Description = "JWT token expiration time in minutes (1440 = 24 hours)", Category = "JWT", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 15, Key = "JWT:RefreshTokenExpirationDays", Value = "7", Description = "Refresh token expiration in days", Category = "JWT", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                
                // Gemini AI Settings
                new SystemSettingEntity { Id = 16, Key = "Gemini:Model", Value = "gemini-2.0-flash-exp", Description = "Gemini AI model to use", Category = "Gemini", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 17, Key = "Gemini:Temperature", Value = "0.7", Description = "Gemini AI temperature (0.0-1.0)", Category = "Gemini", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 18, Key = "Gemini:MaxTokens", Value = "8000", Description = "Maximum tokens for Gemini responses", Category = "Gemini", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 19, Key = "Gemini:Enabled", Value = "true", Description = "Enable/disable Gemini AI features", Category = "Gemini", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                
                // Scraping Settings
                new SystemSettingEntity { Id = 20, Key = "Scraping:BatchSize", Value = "100", Description = "Number of items to process in each batch", Category = "Scraping", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 21, Key = "Scraping:TimeoutSeconds", Value = "30", Description = "HTTP request timeout in seconds", Category = "Scraping", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 22, Key = "Scraping:RetryAttempts", Value = "3", Description = "Number of retry attempts for failed requests", Category = "Scraping", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 23, Key = "Scraping:DelayBetweenRequestsMs", Value = "500", Description = "Delay between scraping requests in milliseconds", Category = "Scraping", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                
                // Pagination Settings
                new SystemSettingEntity { Id = 24, Key = "Pagination:DefaultPageSize", Value = "20", Description = "Default number of items per page", Category = "Pagination", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 25, Key = "Pagination:MaxPageSize", Value = "100", Description = "Maximum allowed page size", Category = "Pagination", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                
                // Analytics Settings
                new SystemSettingEntity { Id = 26, Key = "Analytics:DefaultDaysBack", Value = "90", Description = "Default days to look back for analytics", Category = "Analytics", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 27, Key = "Analytics:MinConfidenceScore", Value = "60", Description = "Minimum confidence score for recommendations", Category = "Analytics", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 28, Key = "Analytics:MaxOpportunities", Value = "50", Description = "Maximum number of opportunities to return", Category = "Analytics", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                
                // Logging Settings
                new SystemSettingEntity { Id = 29, Key = "Logging:EnableFileLogging", Value = "true", Description = "Enable logging to files", Category = "Logging", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 30, Key = "Logging:LogPath", Value = "C:/Temp", Description = "Directory path for log files", Category = "Logging", CreatedAt = DateTime.UtcNow, CreatedBy = "System" },
                new SystemSettingEntity { Id = 31, Key = "Logging:RetentionDays", Value = "30", Description = "Number of days to keep log files", Category = "Logging", CreatedAt = DateTime.UtcNow, CreatedBy = "System" }
            );
        }
    }
}
