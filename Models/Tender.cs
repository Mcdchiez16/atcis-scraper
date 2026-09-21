namespace ZimbabweTenderAPI.Models
{
    public class Tender
    {
        public string Id { get; set; }
        public string TenderId { get; set; }
        public string ReferenceNumber { get; set; }
        public string Title { get; set; }
        public List<string> CategoryCodes { get; set; } = new();
        public List<string> CategoryNames { get; set; } = new();
        public string ProcuringEntity { get; set; }
        public string Scope { get; set; }
        public DateTime? PublishDate { get; set; }
        public DateTime? ClosingDate { get; set; }
        public string DetailsUrl { get; set; }
        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
        public string SourceUrl { get; set; }
        public int PageNumber { get; set; }
    }

    public class TenderBatch
    {
        public List<Tender> Tenders { get; set; } = new();
        public int Page { get; set; }
        public int TotalTenders { get; set; }
        public DateTime ScrapedAt { get; set; }
        public bool HasMorePages { get; set; }
        public int? NextPage { get; set; }
    }
}