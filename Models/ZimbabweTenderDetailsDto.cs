using System;
using System.Collections.Generic;

namespace ZimbabweTenderAPI.Models
{
    public class ZimbabweTenderDetailDto
    {
        public string TenderId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string BidValidityPeriod { get; set; } = string.Empty;
        public string TenderReferenceNumber { get; set; } = string.Empty;
        public string LotType { get; set; } = string.Empty;
        public string ProcurementMethod { get; set; } = string.Empty;
        public string ClassOfProcurement { get; set; } = string.Empty;
        public string ApplicableProcurementRules { get; set; } = string.Empty;
        public string FundingSource { get; set; } = string.Empty;
        public string DeliveryProjectLocation { get; set; } = string.Empty;
        public string RequiredSupplierCategories { get; set; } = string.Empty;
        public string DeliveryPeriod { get; set; } = string.Empty;
        public string ProcuringEntity { get; set; } = string.Empty;
        public string ProcuringEntityAddress { get; set; } = string.Empty;
        public DateTime? DateCreated { get; set; }
        public string ProjectName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ZimbabweLineItemDto> LineItems { get; set; } = new();
        public DateTime? PublishedDate { get; set; }
        public DateTime? ClosingDate { get; set; }
        public DateTime? DateLastUpdated { get; set; }
        public string BidFormFee { get; set; } = string.Empty;
        public string BidSecurityDomestic { get; set; } = string.Empty;
        public string BidSecurityInternational { get; set; } = string.Empty;
        public string EstablishmentAmountDomestic { get; set; } = string.Empty;
        public string EstablishmentAmountInternational { get; set; } = string.Empty;
        public string SpocFee { get; set; } = string.Empty;
        public int TenderAddendums { get; set; }
        public int NumberOfDownloads { get; set; }
        public string DocumentsPreviewUrl { get; set; } = string.Empty;
        public List<ZimbabweTenderDocumentDto> Documents { get; set; } = new();
        public Dictionary<string, string> RawFields { get; set; } = new();
        public string DetailsUrl { get; set; } = string.Empty;
        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
    }

    public class ZimbabweLineItemDto
    {
        public string ItemNumber { get; set; } = string.Empty;
        public string Unspsc { get; set; } = string.Empty;
        public string LotName { get; set; } = string.Empty;
        public string LotDescription { get; set; } = string.Empty;
        public string Quantity { get; set; } = string.Empty;
        public string UnitOfMeasure { get; set; } = string.Empty;
    }

    public class ZimbabweTenderDocumentDto
    {
        public string DocumentId { get; set; } = string.Empty;
        public string TenderId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
    }

    public class PrazSessionRequest
    {
        /// <summary>
        /// The CAKEPHP or session cookie value from a logged-in PRAZ browser session (e.g. CAKEPHP=xyz or raw cookie header)
        /// </summary>
        public string SessionCookie { get; set; } = string.Empty;
    }
}
