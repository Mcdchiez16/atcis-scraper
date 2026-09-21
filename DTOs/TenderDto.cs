using ZimbabweTenderAPI.Models;

namespace ZimbabweTenderAPI.DTOs
{
    public class TenderDto
    {
        public string TenderId { get; set; }
        public string ReferenceNumber { get; set; }
        public string Title { get; set; }
        public List<string> CategoryCodes { get; set; }
        public List<string> CategoryNames { get; set; }
        public string ProcuringEntity { get; set; }
        public string Scope { get; set; }
        public string PublishDate { get; set; }
        public string ClosingDate { get; set; }
        public string DetailsUrl { get; set; }
    }





















    // Update TenderSimilarityDto to include detailed similarity scores
    public class TenderSimilarityDto
    {
        public string TenderId { get; set; }
        public string Title { get; set; }
        public string ReferenceNumber { get; set; }
        public string ProcuringEntity { get; set; }
        public double SimilarityScore { get; set; }
        public List<string> MatchReasons { get; set; } = new List<string>();
        public string TargetTenderId { get; set; }
        public string TargetTitle { get; set; }
        public double CategorySimilarity { get; set; }
        public double EntitySimilarity { get; set; }
        public double TextSimilarity { get; set; }
    }


    public class TenderParticipationDto
    {
        public string CompanyName { get; set; }
        public int TenderCount { get; set; }
        public string TenderId { get; set; }
        public string Title { get; set; }
        public string ReferenceNumber { get; set; }
        public string ProcuringEntity { get; set; }
        public string Role { get; set; }
        public DateTime? PublishDate { get; set; }
        public DateTime? ClosingDate { get; set; }
        //public DateTime? AwardDate { get; set; }
        public string Status { get; set; }
        public List<TenderInfoDto> ParticipatedTenders { get; set; } = new List<TenderInfoDto>();

        public string ParticipationStatus { get; set; } // Won, Lost, Participated
        public string AwardDate { get; set; }
        public string ContractValue { get; set; }
        public double SimilarityScore { get; set; } // Add this
    }

    public class TenderInfoDto
    {
        public string TenderId { get; set; }
        public string Title { get; set; }
        public string ReferenceNumber { get; set; }
        public DateTime? PublishDate { get; set; }
    }


    public class SimilarityAnalysisRequest
    {
        public string TenderId { get; set; }
        public List<string> Keywords { get; set; } = new List<string>();
        public double MinSimilarityThreshold { get; set; } = 0.3;
        public bool IncludeContentAnalysis { get; set; } = false;
    }

    public class CategoryStatsDto
    {
        public string CategoryName { get; set; }
        public int TenderCount { get; set; }
        public double Percentage { get; set; }
    }

    public class CategorySearchRequest
    {
        public string Category { get; set; }
        public double SimilarityThreshold { get; set; } = 0.7;
        public int Limit { get; set; } = 50;
        public bool IncludePastTenders { get; set; } = true;
    }

    public class TenderWithSimilarityDto : Tender
    {
        public double SimilarityScore { get; set; }
        public List<string> MatchReasons { get; set; }
        public double CategorySimilarity { get; set; }
        public double EntitySimilarity { get; set; }
        public double TextSimilarity { get; set; }
        public string TargetCategory { get; set; }
    }

















    public class ApiResponse<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public T Data { get; set; }
        public int TotalCount { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        // Optional pagination properties
        public int? PageNumber { get; set; }
        public int? PageSize { get; set; }
        public int? TotalPages { get; set; }
        public bool? HasPreviousPage { get; set; }
        public bool? HasNextPage { get; set; }
    }

    public class PaginatedResponse<T>
    {
        public List<T> Items { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public int TotalCount { get; set; }
        public bool HasPreviousPage { get; set; }
        public bool HasNextPage { get; set; }
    }


    // Updated AwardNoticeDto with Id and ParsedAwardDate
    public class AwardNoticeDto
    {
        public int Id { get; set; }  // ADD THIS - Database ID
        public string AwardNoticeNumber { get; set; }
        public string TenderId { get; set; }
        public string AwardTitle { get; set; }
        public string Awardee { get; set; }
        public string AwardDate { get; set; }
        public DateTime? ParsedAwardDate { get; set; }  // ADD THIS - Parsed date for sorting/filtering
        public string DetailsUrl { get; set; }
        public string Currency { get; set; }
        public decimal? ContractValue { get; set; }
    }
    // Annual Procurement Plan DTO
    public class AnnualProcurementPlanDto
    {
        public int Id { get; set; } // ✅ Added database ID
        public string ProcuringEntity { get; set; }
        public string Year { get; set; }
        public string ViewAppUrl { get; set; }
        public int TotalItems { get; set; }
        public decimal TotalEstimatedValue { get; set; }
        public DateTime LastScrapedAt { get; set; }
    }

   /* // Tender Similarity DTO
    public class TenderSimilarityDto
    {
        public string TenderId { get; set; }
        public string Title { get; set; }
        public string ReferenceNumber { get; set; }
        public double SimilarityScore { get; set; }
        public List<string> SimilarityReasons { get; set; }
    }

    // Tender Participation DTO
    public class TenderParticipationDto
    {
        public string TenderId { get; set; }
        public string Title { get; set; }
        public string CompanyName { get; set; }
        public string ParticipationStatus { get; set; } // Won, Lost, Participated
        public string AwardDate { get; set; }
        public string ContractValue { get; set; }
    }

    // Similarity Analysis Request
    public class SimilarityAnalysisRequest
    {
        public string TenderId { get; set; }
        public List<string> Keywords { get; set; }
        public double MinSimilarityThreshold { get; set; } = 0.7;
        public bool IncludeContentAnalysis { get; set; } = true;
    }*/

    // Company Participation Request
    public class CompanyParticipationRequest
    {
        public string CompanyName { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public List<string> ProcuringEntities { get; set; }
        public int Limit { get; set; } = 20;
    }

    // Award Notice Search Request
    public class AwardNoticeSearchRequest
    {
        public string AwardNoticeNumber { get; set; }
        public string TenderId { get; set; }
        public string AwardTitle { get; set; }
        public string Awardee { get; set; }
        public DateTime? AwardDateFrom { get; set; }
        public DateTime? AwardDateTo { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    // Annual Procurement Plan Search Request
    public class AnnualProcurementPlanSearchRequest
    {
        public string ProcuringEntity { get; set; }
        public string Year { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }


    public class AnnualProcurementPlanDetailDto
    {
        public string Title { get; set; }
        public string ProcuringEntity { get; set; }
        public string Year { get; set; }
        public string SourceUrl { get; set; }
        public List<ProcurementPlanItem> Items { get; set; }
        public int TotalItems { get; set; }
        public decimal TotalEstimatedValue { get; set; }
        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
    }

    public class ProcurementPlanItem
    {
        public string ItemId { get; set; }
        public string RefNo { get; set; }
        public string ClassOfProcurement { get; set; }
        public string ObjectCode { get; set; }
        public string Description { get; set; }
        public string PmoEndUser { get; set; }
        public string ProcurementMethod { get; set; }
        public string EoiPublicationDate { get; set; }
        public string EoiClosingDate { get; set; }
        public string TenderPublicationDate { get; set; }
        public string BidClosingDate { get; set; }
        public string AwardNoticeDate { get; set; }
        public string ContractSigningDate { get; set; }
        public string CycleDays { get; set; }
        public string LeadTime { get; set; }
        public string Spoc { get; set; }
        public string SourceOfFunds { get; set; }
        public string UnitOfMeasurement { get; set; }
        public string Quantity { get; set; }
        public string Comments { get; set; }
        public bool IsSupplement { get; set; }

        // Parsed dates
        public DateTime? ParsedTenderPublicationDate { get; set; }
        public DateTime? ParsedBidClosingDate { get; set; }
        public DateTime? ParsedAwardNoticeDate { get; set; }
        public DateTime? ParsedContractSigningDate { get; set; }
    }

    public class TenderDocumentAnalysisRequest
    {
        public string TenderId { get; set; } = string.Empty;
        public string? DocumentUrl { get; set; }
        public string? FileName { get; set; }
        public string? CustomPrompt { get; set; }
    }

    public class StatutoryChecklistItem
    {
        public string Item { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class TechnicalCriterionItem
    {
        public string Criterion { get; set; } = string.Empty;
        public string Weight { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class RiskFactorItem
    {
        public string Risk { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Mitigation { get; set; } = string.Empty;
    }

    public class TenderDocumentAnalysisResponse
    {
        public string TenderId { get; set; } = string.Empty;
        public string TenderTitle { get; set; } = string.Empty;
        public string ProcuringEntity { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ExecutiveSummary { get; set; } = string.Empty;
        public List<string> KeyDeliverables { get; set; } = new();
        public List<StatutoryChecklistItem> MandatoryChecklist { get; set; } = new();
        public List<TechnicalCriterionItem> EvaluationMatrix { get; set; } = new();
        public Dictionary<string, string> CommercialTerms { get; set; } = new();
        public List<RiskFactorItem> RiskAssessment { get; set; } = new();
        public string PricingStrategy { get; set; } = string.Empty;
        public int AiFitScore { get; set; } = 85;
        public string BidDecision { get; set; } = "STRONG_BID";
        public string RawAiResponse { get; set; } = string.Empty;
        public List<RequiredItemSpecification> RequiredItems { get; set; } = new();
    }

    public class RequiredItemSpecification
    {
        public string ItemNumber { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string Specifications { get; set; } = string.Empty;
        public string Quantity { get; set; } = string.Empty;
        public string Unit { get; set; } = string.Empty;
        public decimal? EstimatedUnitCost { get; set; }
        public decimal? TotalEstimatedCost { get; set; }
        public string ComplianceStandards { get; set; } = string.Empty;
        public List<SuggestedSupplierMatch> SuggestedSuppliers { get; set; } = new();
    }

    public class SuggestedSupplierMatch
    {
        public string SupplierName { get; set; } = string.Empty;
        public string Tier { get; set; } = string.Empty;
        public int MatchScore { get; set; } = 95;
        public string Location { get; set; } = string.Empty;
        public string LeadTime { get; set; } = string.Empty;
        public decimal? EstimatedPrice { get; set; }
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;
        public bool PrazRegistered { get; set; } = true;
        public string Notes { get; set; } = string.Empty;
    }
}