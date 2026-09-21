using System;
using System.Collections.Generic;

namespace ZimbabweTenderAPI.Models
{
    public class ZambiaTender
    {
        public string Id { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ReferenceNumber { get; set; } = string.Empty;
        public string ProcuringEntity { get; set; } = string.Empty;
        public DateTime? SubmissionDeadline { get; set; }
        public string ProcurementMethod { get; set; } = string.Empty;
        public string OpenedBidsUrl { get; set; } = string.Empty;
        public DateTime? AwardDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public string DetailsUrl { get; set; } = string.Empty;
        public int PageNumber { get; set; }
        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
        public string SourceCountry { get; set; } = "Zambia";
        public string SourceUrl { get; set; } = string.Empty;
    }

    public class ZambiaTenderBatch
    {
        public List<ZambiaTender> Tenders { get; set; } = new();
        public int Page { get; set; }
        public int TotalTenders { get; set; }
        public int TotalPages { get; set; }
        public bool HasMorePages { get; set; }
        public int? NextPage { get; set; }
        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
    }

    public class ZambiaTenderDocumentDto
    {
        public string DocumentId { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public string AddendumId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Language { get; set; } = "EN";
        public string DownloadUrl { get; set; } = string.Empty;
    }

    public class ZambiaTenderDetailDto
    {
        public string ResourceId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ProcuringEntity { get; set; } = string.Empty;
        public string ProcuringEntityId { get; set; } = string.Empty;
        public string AppReferenceNumber { get; set; } = string.Empty;
        public string TenderUniqueId { get; set; } = string.Empty;
        public string DeadlineRemaining { get; set; } = string.Empty;
        public DateTime? SubmissionDeadline { get; set; }
        public DateTime? BidOpeningDate { get; set; }
        public DateTime? ClarificationDeadline { get; set; }
        public DateTime? PublicationDate { get; set; }
        public DateTime? ContractNoticeDate { get; set; }
        public string ProcurementType { get; set; } = string.Empty;
        public string Procedure { get; set; } = string.Empty;
        public string CommencementType { get; set; } = string.Empty;
        public string Threshold { get; set; } = string.Empty;
        public string ProcurementTechnique { get; set; } = string.Empty;
        public int NumberOfStages { get; set; } = 1;
        public string EvaluationMechanism { get; set; } = string.Empty;
        public string CeecPreferenceType { get; set; } = string.Empty;
        public bool FrameworkAgreement { get; set; }
        public bool Postqualification { get; set; }
        public List<string> UnspscCodes { get; set; } = new();
        public string PaymentType { get; set; } = string.Empty;
        public string PaymentAmount { get; set; } = string.Empty;
        public string PaymentTerms { get; set; } = string.Empty;
        public string BidSecurityType { get; set; } = string.Empty;
        public bool AwardedInLots { get; set; }
        public List<string> Lots { get; set; } = new();
        public List<ZambiaTenderDocumentDto> Documents { get; set; } = new();
        public Dictionary<string, string> RawFields { get; set; } = new();
        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
        public string DetailsUrl { get; set; } = string.Empty;
    }

    public class ZambiaPublicationPlanDto
    {
        public string SubmissionId { get; set; } = string.Empty;
        public string OrganisationName { get; set; } = string.Empty;
        public string OrganisationId { get; set; } = string.Empty;
        public DateTime? PublishedAt { get; set; }
        public string DownloadUrl { get; set; } = string.Empty;
        public string CycleId { get; set; } = string.Empty;
    }

    public class ZambiaPublicationBatch
    {
        public List<ZambiaPublicationPlanDto> Items { get; set; } = new();
        public string CycleId { get; set; } = string.Empty;
        public string CycleDescription { get; set; } = string.Empty;
        public int Page { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public bool HasMorePages { get; set; }
        public int? NextPage { get; set; }
        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
    }

    public class ZambiaDocumentAnalysisRequest
    {
        public string? DocumentUrl { get; set; }
        public string? DocumentText { get; set; }
        public string? TenderTitle { get; set; }
        public string? ProcuringEntity { get; set; }
        public List<string>? FocusAreas { get; set; }
    }

    public class ZambiaDocumentAnalysisResponse
    {
        public string TenderTitle { get; set; } = string.Empty;
        public string ExecutiveSummary { get; set; } = string.Empty;
        public List<string> KeyRequirements { get; set; } = new();
        public List<string> MandatoryEligibility { get; set; } = new();
        public List<string> EvaluationCriteria { get; set; } = new();
        public List<string> ChecklistItems { get; set; } = new();
        public List<string> IdentifiedRisks { get; set; } = new();
        public string RecommendedAction { get; set; } = string.Empty;
        public DateTime AnalysisTimestamp { get; set; } = DateTime.UtcNow;
    }

    public class AttachZambiaDocumentRequest
    {
        public int? TargetTenderId { get; set; }
        public string DocumentName { get; set; } = string.Empty;
        public string DocumentType { get; set; } = "TenderDossier"; // TenderDossier, ProcurementPlan, TOR, Specification
        public string? Description { get; set; }
        public string RemoteFileUrl { get; set; } = string.Empty;
    }
}
