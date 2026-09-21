using System;
using System.Collections.Generic;

namespace ZimbabweTenderAPI.Models
{
    /// <summary>
    /// Unified tender model aggregating opportunities across multiple national and international portals.
    /// </summary>
    public class MultiSourceTenderDto
    {
        public string SourcePortal { get; set; } = string.Empty; // "GoZambiaJobs", "OnlineTenders", "WorldBank", "UNProcurement", "EUFunding", "DevelopmentAid", "ZPPA"
        public string TenderId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ProcuringEntity { get; set; } = string.Empty;
        public string Country { get; set; } = "Zambia";
        public string Location { get; set; } = string.Empty;
        public string ProcurementType { get; set; } = "Tender"; // "Tender", "RFP", "EOI", "Grant", "Contract Award"
        public string CommodityGroup { get; set; } = string.Empty;
        public DateTime? PublishDate { get; set; }
        public DateTime? ClosingDate { get; set; }
        public string SourceUrl { get; set; } = string.Empty;
        public string? PdfUrl { get; set; }
        public List<string> DocumentUrls { get; set; } = new List<string>();
        public string Description { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactName { get; set; } = string.Empty;
        public string EstimatedValue { get; set; } = string.Empty;
        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// World Bank Procurement Notice model
    /// </summary>
    public class WorldBankNoticeDto
    {
        public string Id { get; set; } = string.Empty;
        public string NoticeType { get; set; } = string.Empty;
        public string NoticeDate { get; set; } = string.Empty;
        public string NoticeStatus { get; set; } = string.Empty;
        public string SubmissionDeadlineDate { get; set; } = string.Empty;
        public string SubmissionDeadlineTime { get; set; } = string.Empty;
        public string ProjectCountryName { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string BidReferenceNo { get; set; } = string.Empty;
        public string BidDescription { get; set; } = string.Empty;
        public string ProcurementGroup { get; set; } = string.Empty;
        public string ProcurementMethodCode { get; set; } = string.Empty;
        public string ProcurementMethodName { get; set; } = string.Empty;
        public string ContactName { get; set; } = string.Empty;
        public string ContactOrganization { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactPhoneNo { get; set; } = string.Empty;
        public string NoticeText { get; set; } = string.Empty;
        public string NoticeUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// United Nations Global Procurement Notice model (Tenders & EOIs)
    /// </summary>
    public class UnProcurementNoticeDto
    {
        public string NoticeNumber { get; set; } = string.Empty; // Bid No. or EOI No.
        public string Title { get; set; } = string.Empty;
        public string NoticeType { get; set; } = "Tender"; // "Tender" or "EOI"
        public string CommodityGroup { get; set; } = string.Empty;
        public string OpeningDate { get; set; } = string.Empty;
        public string ExpiryDate { get; set; } = string.Empty;
        public string OpeningTime { get; set; } = string.Empty;
        public string DetailsPdfUrl { get; set; } = string.Empty;
        public DateTime? ParsedOpeningDate { get; set; }
        public DateTime? ParsedExpiryDate { get; set; }
    }

    /// <summary>
    /// EU Funding & Tenders Portal Notice model
    /// </summary>
    public class EuFundingNoticeDto
    {
        public string Identifier { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Programme { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string StartDate { get; set; } = string.Empty;
        public string DeadlineDate { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Type { get; set; } = "Call for Tenders";
    }

    /// <summary>
    /// GoZambiaJobs Tender Notice model
    /// </summary>
    public class GoZambiaNoticeDto
    {
        public string Guid { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Company { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string PubDate { get; set; } = string.Empty;
        public string Link { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<string> DocumentLinks { get; set; } = new List<string>();
        public DateTime? ParsedPubDate { get; set; }
    }

    /// <summary>
    /// OnlineTenders Zambia Notice model
    /// </summary>
    public class OnlineTendersNoticeDto
    {
        public string ReferenceNumber { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string ClosingDate { get; set; } = string.Empty;
        public string Country { get; set; } = "Zambia";
        public string SourceUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Aggregated Procurement Search Request filter
    /// </summary>
    public class AggregatedProcurementSearchRequest
    {
        public string? Keyword { get; set; }
        public string? Country { get; set; }
        public string? Portal { get; set; } // "all", "WorldBank", "UNProcurement", "GoZambiaJobs", "OnlineTenders", "EUFunding"
        public int Limit { get; set; } = 50;
    }
}
