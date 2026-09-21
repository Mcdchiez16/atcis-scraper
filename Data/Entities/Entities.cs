// ZimbabweTenderAPI.Data.Entities (UPDATED)
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace ZimbabweTenderAPI.Data.Entities
{
    // Base audit entity for all database entities
    public abstract class AuditableEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(100)]
        public string CreatedBy { get; set; } = "System";

        public DateTime? UpdatedAt { get; set; }

        [MaxLength(100)]
        public string? UpdatedBy { get; set; }

        public bool IsDeleted { get; set; } = false;

        public DateTime? DeletedAt { get; set; }

        [MaxLength(100)]
        public string? DeletedBy { get; set; }

        // For tracking version/changes
        [Timestamp]
        public byte[]? RowVersion { get; set; }
    }

    // Live Tender Entity (UPDATED COLUMN TYPES)
    [Table("LiveTenders")]
    [Index(nameof(TenderId), IsUnique = true)]
    [Index(nameof(ReferenceNumber))]
    [Index(nameof(ProcuringEntity))]
    [Index(nameof(ClosingDate))]
    public class LiveTenderEntity : AuditableEntity
    {
        [Required]
        [MaxLength(50)]
        public string TenderId { get; set; }

        [MaxLength(200)]
        public string ReferenceNumber { get; set; }

        [Required]
        [MaxLength(1000)]
        public string Title { get; set; }

        // Mapped to NVARCHAR(MAX) manually, now reflected in attributes
        [Column(TypeName = "nvarchar(max)")]
        public string CategoryCodes { get; set; } // Stored as JSON

        // Mapped to NVARCHAR(MAX) manually, now reflected in attributes
        [Column(TypeName = "nvarchar(max)")]
        public string CategoryNames { get; set; } // Stored as JSON

        [Required]
        [MaxLength(1000)]
        public string ProcuringEntity { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string Scope { get; set; }

        public DateTime? PublishDate { get; set; }

        public DateTime? ClosingDate { get; set; }

        // Mapped to NVARCHAR(MAX) manually, now reflected in attributes
        [Column(TypeName = "nvarchar(max)")]
        public string DetailsUrl { get; set; }

        // Mapped to NVARCHAR(MAX) manually, now reflected in attributes
        [Column(TypeName = "nvarchar(max)")]
        public string SourceUrl { get; set; }

        public int PageNumber { get; set; }

        public DateTime LastScrapedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastVerifiedAt { get; set; }

        // Flag to indicate if tender moved to closed
        public bool MovedToClosed { get; set; } = false;

        public DateTime? MovedToClosedAt { get; set; }

        // Navigation property for audit trail
        public virtual ICollection<TenderAuditLog> AuditLogs { get; set; }
    }




    // Fixed ClosedTenderEntity - Remove index from nvarchar(max) column
    [Table("ClosedTenders")]
    [Index(nameof(TenderId), IsUnique = true)]
    [Index(nameof(ReferenceNumber))]
    // REMOVED: [Index(nameof(ProcuringEntity))] - Cannot index nvarchar(max)
    [Index(nameof(ClosingDate))]
    public class ClosedTenderEntity : AuditableEntity
    {
        [Required]
        [MaxLength(50)]
        public string TenderId { get; set; }

        [MaxLength(200)]
        public string? ReferenceNumber { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(max)")]
        public string Title { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? CategoryCodes { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? CategoryNames { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(max)")]
        public string ProcuringEntity { get; set; } // No index - it's nvarchar(max)

        [Column(TypeName = "nvarchar(max)")]
        public string? Scope { get; set; }

        public DateTime? PublishDate { get; set; }
        public DateTime? ClosingDate { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? DetailsUrl { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? SourceUrl { get; set; }

        public int PageNumber { get; set; }

        [MaxLength(100)]
        public string? Status { get; set; }

        public DateTime? ActualClosingDate { get; set; }
        public DateTime LastScrapedAt { get; set; } = DateTime.UtcNow;
        public int? AwardNoticeId { get; set; }

        [ForeignKey(nameof(AwardNoticeId))]
        public virtual AwardNoticeEntity? AwardNotice { get; set; }

        public virtual ICollection<TenderAuditLog>? AuditLogs { get; set; }
    }

    // Fixed AwardNoticeEntity - Remove index from nvarchar(max) column
    [Table("AwardNotices")]
    [Index(nameof(AwardNoticeNumber), IsUnique = true)]
    [Index(nameof(TenderId))]
    // REMOVED: [Index(nameof(Awardee))] - Cannot index nvarchar(max)
    public class AwardNoticeEntity : AuditableEntity
    {
        [Required]
        [MaxLength(100)]
        public string AwardNoticeNumber { get; set; }

        [Required]
        [MaxLength(50)]
        public string TenderId { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(max)")]
        public string AwardTitle { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(max)")]
        public string Awardee { get; set; }

        [Required]
        [MaxLength(100)]
        public string AwardDate { get; set; }

        public DateTime? ParsedAwardDate { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(max)")]
        public string DetailsUrl { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? ContractValue { get; set; }

        [Required]
        [MaxLength(50)]
        public string Currency { get; set; } = "USD";

        [Required]
        public DateTime LastScrapedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<AwardNoticeAuditLog>? AuditLogs { get; set; }
        public virtual ICollection<ClosedTenderEntity>? ClosedTenders { get; set; }
    }

    // Fixed ProcurementPlanEntity - Reduce ProcuringEntity length
    [Table("ProcurementPlans")]
    [Index(nameof(ProcuringEntity), nameof(Year), IsUnique = true)]
    public class ProcurementPlanEntity : AuditableEntity
    {
        [Required]
        [MaxLength(450)] // Reduced from 898 to fit in SQL Server index limit (900 bytes)
        public string ProcuringEntity { get; set; }

        [Required]
        [MaxLength(10)]
        public string Year { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? ViewAppUrl { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? Title { get; set; }

        public int TotalItems { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalEstimatedValue { get; set; }

        public DateTime LastScrapedAt { get; set; } = DateTime.UtcNow;

        public virtual ICollection<ProcurementPlanItemEntity>? Items { get; set; }
        public virtual ICollection<ProcurementPlanAuditLog>? AuditLogs { get; set; }
    }

    // Fixed ScrapingJobHistoryEntity - Make ErrorDetails nullable
    [Table("ScrapingJobHistory")]
    [Index(nameof(JobType), nameof(StartTime))]
    public class ScrapingJobHistoryEntity
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string JobType { get; set; }

        [Required]
        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        public DateTime? EndTime { get; set; }

        [MaxLength(50)]
        public string? Status { get; set; }

        public int ItemsProcessed { get; set; }
        public int ItemsAdded { get; set; }
        public int ItemsUpdated { get; set; }
        public int ItemsFailed { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? ErrorDetails { get; set; } // ✅ NOW NULLABLE

        [Column(TypeName = "nvarchar(max)")]
        public string? Summary { get; set; }

        [MaxLength(100)]
        public string? TriggeredBy { get; set; }
    }



    // Procurement Plan Item Entity (No changes needed)
    [Table("ProcurementPlanItems")]
    [Index(nameof(ProcurementPlanId), nameof(RefNo))]
    public class ProcurementPlanItemEntity : AuditableEntity
    {
        public int ProcurementPlanId { get; set; }

        [ForeignKey(nameof(ProcurementPlanId))]
        public virtual ProcurementPlanEntity ProcurementPlan { get; set; }

        [MaxLength(100)]
        public string ItemId { get; set; }

        [MaxLength(100)]
        public string RefNo { get; set; }

        [MaxLength(200)]
        public string ClassOfProcurement { get; set; }

        [MaxLength(100)]
        public string ObjectCode { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string Description { get; set; }

        [MaxLength(200)]
        public string PmoEndUser { get; set; }

        [MaxLength(100)]
        public string ProcurementMethod { get; set; }

        [MaxLength(50)]
        public string EoiPublicationDate { get; set; }

        [MaxLength(50)]
        public string EoiClosingDate { get; set; }

        [MaxLength(50)]
        public string TenderPublicationDate { get; set; }

        [MaxLength(50)]
        public string BidClosingDate { get; set; }

        [MaxLength(50)]
        public string AwardNoticeDate { get; set; }

        [MaxLength(50)]
        public string ContractSigningDate { get; set; }

        [MaxLength(50)]
        public string CycleDays { get; set; }

        [MaxLength(50)]
        public string LeadTime { get; set; }

        [MaxLength(200)]
        public string Spoc { get; set; }

        [MaxLength(200)]
        public string SourceOfFunds { get; set; }

        [MaxLength(50)]
        public string UnitOfMeasurement { get; set; }

        [MaxLength(50)]
        public string Quantity { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string Comments { get; set; }

        public bool IsSupplement { get; set; }

        // Parsed dates for better querying
        public DateTime? ParsedTenderPublicationDate { get; set; }
        public DateTime? ParsedBidClosingDate { get; set; }
        public DateTime? ParsedAwardNoticeDate { get; set; }
        public DateTime? ParsedContractSigningDate { get; set; }
    }

    // Tender Audit Log (FIXED NULLABILITY)
    [Table("TenderAuditLogs")]
    [Index(nameof(TenderId), nameof(ChangeDate))]
    public class TenderAuditLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string TenderId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Action { get; set; } // Created, Updated, Deleted, MovedToClosed

        [Column(TypeName = "nvarchar(max)")]
        public string? Changes { get; set; } // MADE NULLABLE

        [Column(TypeName = "nvarchar(max)")]
        public string? OldValues { get; set; } // MADE NULLABLE

        [Column(TypeName = "nvarchar(max)")]
        public string? NewValues { get; set; } // MADE NULLABLE

        [Required]
        public DateTime ChangeDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(100)]
        public string ChangedBy { get; set; }

        [MaxLength(200)]
        public string? IPAddress { get; set; } // MADE NULLABLE

        [MaxLength(500)]
        public string? UserAgent { get; set; } // MADE NULLABLE

        // Navigation properties
        public int? LiveTenderId { get; set; }
        [ForeignKey(nameof(LiveTenderId))]
        public virtual LiveTenderEntity LiveTender { get; set; }

        public int? ClosedTenderId { get; set; }
        [ForeignKey(nameof(ClosedTenderId))]
        public virtual ClosedTenderEntity ClosedTender { get; set; }
    }

    // Award Notice Audit Log (FIXED NULLABILITY)
    [Table("AwardNoticeAuditLogs")]
    [Index(nameof(AwardNoticeNumber), nameof(ChangeDate))]
    public class AwardNoticeAuditLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string AwardNoticeNumber { get; set; }

        [Required]
        [MaxLength(50)]
        public string Action { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? Changes { get; set; } // MADE NULLABLE

        [Column(TypeName = "nvarchar(max)")]
        public string? OldValues { get; set; } // MADE NULLABLE

        [Column(TypeName = "nvarchar(max)")]
        public string? NewValues { get; set; } // MADE NULLABLE

        [Required]
        public DateTime ChangeDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(100)]
        public string ChangedBy { get; set; }

        [MaxLength(200)]
        public string? IPAddress { get; set; } // MADE NULLABLE

        [MaxLength(500)]
        public string? UserAgent { get; set; } // MADE NULLABLE

        public int AwardNoticeId { get; set; }

        [ForeignKey(nameof(AwardNoticeId))]
        public virtual AwardNoticeEntity AwardNotice { get; set; }
    }

    // Procurement Plan Audit Log (FIXED NULLABILITY)
    [Table("ProcurementPlanAuditLogs")]
    [Index(nameof(ProcurementPlanId), nameof(ChangeDate))]
    public class ProcurementPlanAuditLog
    {
        [Key]
        public int Id { get; set; }

        public int ProcurementPlanId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Action { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? Changes { get; set; } // MADE NULLABLE

        [Column(TypeName = "nvarchar(max)")]
        public string? OldValues { get; set; } // MADE NULLABLE

        [Column(TypeName = "nvarchar(max)")]
        public string? NewValues { get; set; } // MADE NULLABLE

        [Required]
        public DateTime ChangeDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(100)]
        public string ChangedBy { get; set; }

        [MaxLength(200)]
        public string? IPAddress { get; set; } // MADE NULLABLE

        [MaxLength(500)]
        public string? UserAgent { get; set; } // MADE NULLABLE

        [ForeignKey(nameof(ProcurementPlanId))]
        public virtual ProcurementPlanEntity ProcurementPlan { get; set; }
    }

    // Role Entity
    [Table("Roles")]
    [Index(nameof(Name), IsUnique = true)]
    public class RoleEntity : AuditableEntity
    {
        [Required]
        [MaxLength(50)]
        public string Name { get; set; } // Admin, Guest, Supervisor, HR, ProjectManager, Employee, Development

        [MaxLength(200)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        // Navigation property
        public virtual ICollection<UserRoleEntity>? UserRoles { get; set; }
    }

    // User-Role Mapping (Many-to-Many)
    [Table("UserRoles")]
    [Index(nameof(UserId), nameof(RoleId), IsUnique = true)]
    public class UserRoleEntity
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        public int RoleId { get; set; }

        [Required]
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(100)]
        public string AssignedBy { get; set; } = "System";

        [ForeignKey(nameof(UserId))]
        public virtual UserEntity User { get; set; }

        [ForeignKey(nameof(RoleId))]
        public virtual RoleEntity Role { get; set; }
    }

    // User Entity for Authentication (UPDATED for multiple roles)
    [Table("Users")]
    [Index(nameof(Email), IsUnique = true)]
    [Index(nameof(Username), IsUnique = true)]
    public class UserEntity : AuditableEntity
    {
        [Required]
        [MaxLength(100)]
        public string Username { get; set; }

        [Required]
        [MaxLength(200)]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [MaxLength(500)]
        public string PasswordHash { get; set; }

        [MaxLength(100)]
        public string? FirstName { get; set; }

        [MaxLength(100)]
        public string? LastName { get; set; }

        // REMOVED single Role property - now handled by UserRoles relationship
        // [MaxLength(50)]
        // public string Role { get; set; } = "User"; 

        public bool IsActive { get; set; } = true;

        public bool EmailConfirmed { get; set; } = false;

        [MaxLength(500)]
        public string? RefreshToken { get; set; }

        public DateTime? RefreshTokenExpiryTime { get; set; }

        public DateTime? LastLoginAt { get; set; }

        [MaxLength(200)]
        public string? LastLoginIP { get; set; }

        public int FailedLoginAttempts { get; set; } = 0;

        public DateTime? LockoutEnd { get; set; }

        // Navigation properties
        public virtual ICollection<UserRoleEntity>? UserRoles { get; set; }
        public virtual ICollection<UserAuditLog>? AuditLogs { get; set; }
    }

    // User Audit Log (FIXED NULLABILITY)
    [Table("UserAuditLogs")]
    [Index(nameof(UserId), nameof(ChangeDate))]
    public class UserAuditLog
    {
        [Key]
        public int Id { get; set; }

        public int UserId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Action { get; set; } // Login, Logout, Register, PasswordChange, ProfileUpdate

        [Column(TypeName = "nvarchar(max)")]
        public string? Details { get; set; } // MADE NULLABLE (assuming Details might be null for simple actions)

        [Required]
        public DateTime ChangeDate { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(100)]
        public string ChangedBy { get; set; } // Added [Required] to match logic (it relies on being set)

        [MaxLength(200)]
        public string? IPAddress { get; set; } // MADE NULLABLE

        [MaxLength(500)]
        public string? UserAgent { get; set; } // MADE NULLABLE

        [ForeignKey(nameof(UserId))]
        public virtual UserEntity User { get; set; }
    }

    // System Configuration (No changes needed)
    [Table("SystemConfigurations")]
    [Index(nameof(Key), IsUnique = true)]
    public class SystemConfigurationEntity : AuditableEntity
    {
        [Required]
        [MaxLength(100)]
        public string Key { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(max)")]
        public string Value { get; set; }

        [MaxLength(500)]
        public string Description { get; set; }

        [MaxLength(50)]
        public string Category { get; set; } // Scraping, Email, API, etc.
    }

    // System Settings - Database-driven configuration
    [Table("SystemSettings")]
    [Index(nameof(Key), IsUnique = true)]
    public class SystemSettingEntity : AuditableEntity
    {
        [Required]
        [MaxLength(200)]
        public string Key { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "nvarchar(max)")]
        public string Value { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        [MaxLength(100)]
        public string Category { get; set; } = "General";
    }

    // Tender Assignment Entity - For assigning tenders to users
    [Table("TenderAssignments")]
    [Index(nameof(TenderId), nameof(AssignedToUserId))]
    public class TenderAssignmentEntity : AuditableEntity
    {
        [Required]
        public int TenderId { get; set; } // Can be LiveTender or ClosedTender

        [Required]
        [MaxLength(20)]
        public string TenderType { get; set; } // "Live" or "Closed"

        [Required]
        public int AssignedToUserId { get; set; }

        [Required]
        public int AssignedByUserId { get; set; }

        public DateTime AssignedDate { get; set; } = DateTime.UtcNow;

        public DateTime? DueDate { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending"; // Pending, InProgress, Completed, Cancelled

        [Column(TypeName = "nvarchar(max)")]
        public string? Notes { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? AssignmentInstructions { get; set; }

        public bool EmailSent { get; set; } = false;

        public DateTime? EmailSentAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        [MaxLength(500)]
        public string? CompletionNotes { get; set; }

        // Navigation properties
        [JsonIgnore]
        [ForeignKey(nameof(AssignedToUserId))]
        public virtual UserEntity AssignedToUser { get; set; }

        [JsonIgnore]
        [ForeignKey(nameof(AssignedByUserId))]
        public virtual UserEntity AssignedByUser { get; set; }
    }

    // Tender Document Entity - For uploaded documents
    [Table("TenderDocuments")]
    [Index(nameof(TenderId), nameof(TenderType))]
    public class TenderDocumentEntity : AuditableEntity
    {
        [Required]
        public int TenderId { get; set; }

        [Required]
        [MaxLength(20)]
        public string TenderType { get; set; } // "Live" or "Closed"

        [Required]
        [MaxLength(200)]
        public string DocumentName { get; set; }

        [Required]
        [MaxLength(100)]
        public string DocumentType { get; set; } // PRAZ Certificate, Tender Response, Technical Proposal, etc.

        [Required]
        [MaxLength(500)]
        public string FilePath { get; set; }

        [MaxLength(100)]
        public string? FileExtension { get; set; }

        public long FileSize { get; set; } // in bytes

        [MaxLength(100)]
        public string? MimeType { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? Description { get; set; }

        [Required]
        public int UploadedByUserId { get; set; }

        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

        [MaxLength(100)]
        public string? DocumentVersion { get; set; }

        public bool IsLatestVersion { get; set; } = true;

        // Navigation properties
        [JsonIgnore]
        [ForeignKey(nameof(UploadedByUserId))]
        public virtual UserEntity UploadedByUser { get; set; }
    }

    // Tender Checklist Item Entity - For tracking tender requirements
    [Table("TenderChecklistItems")]
    [Index(nameof(TenderId), nameof(TenderType))]
    public class TenderChecklistItemEntity : AuditableEntity
    {
        [Required]
        public int TenderId { get; set; }

        [Required]
        [MaxLength(20)]
        public string TenderType { get; set; } // "Live" or "Closed"

        [Required]
        [MaxLength(500)]
        public string ItemTitle { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? ItemDescription { get; set; }

        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Pending"; // Pending, InProgress, Completed, NotApplicable

        public bool IsRequired { get; set; } = true;

        public int OrderIndex { get; set; } // For sorting checklist items

        [MaxLength(200)]
        public string? Category { get; set; } // Documentation, Compliance, Technical, Financial, etc.

        public DateTime? CompletedDate { get; set; }

        public int? CompletedByUserId { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? CompletionNotes { get; set; }

        public DateTime? DueDate { get; set; }

        // Navigation properties
        [JsonIgnore]
        [ForeignKey(nameof(CompletedByUserId))]
        public virtual UserEntity? CompletedByUser { get; set; }
    }

    // Tender Approval Entity - For approval workflow
    [Table("TenderApprovals")]
    [Index(nameof(TenderId), nameof(TenderType))]
    [Index(nameof(ApprovalStatus))]
    public class TenderApprovalEntity : AuditableEntity
    {
        [Required]
        public int TenderId { get; set; }

        [Required]
        [MaxLength(20)]
        public string TenderType { get; set; } // "Live" or "Closed"

        [Required]
        [MaxLength(50)]
        public string ApprovalStage { get; set; } // Initial Review, Technical Review, Financial Review, Final Approval, etc.

        [Required]
        public int ApproverUserId { get; set; }

        [Required]
        [MaxLength(50)]
        public string ApprovalStatus { get; set; } = "Pending"; // Pending, Approved, Rejected, RequestChanges

        public DateTime RequestedDate { get; set; } = DateTime.UtcNow;

        public DateTime? ReviewedDate { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? ApproverComments { get; set; }

        [Column(TypeName = "nvarchar(max)")]
        public string? RequestDetails { get; set; }

        public int RequestedByUserId { get; set; }

        public int OrderIndex { get; set; } // For sequential approval workflow

        public bool IsRequired { get; set; } = true;

        // Reference to previous approval in the chain
        public int? PreviousApprovalId { get; set; }

        // Navigation properties
        [JsonIgnore]
        [ForeignKey(nameof(ApproverUserId))]
        public virtual UserEntity ApproverUser { get; set; }

        [JsonIgnore]
        [ForeignKey(nameof(RequestedByUserId))]
        public virtual UserEntity RequestedByUser { get; set; }

        [JsonIgnore]
        [ForeignKey(nameof(PreviousApprovalId))]
        public virtual TenderApprovalEntity? PreviousApproval { get; set; }
    }

}