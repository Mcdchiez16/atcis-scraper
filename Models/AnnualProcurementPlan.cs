namespace ZimbabweTenderAPI.Models
{
    public class AnnualProcurementPlan
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ProcuringEntity { get; set; }
        public int Year { get; set; }
        public string DetailsUrl { get; set; }
        public DateTime ScrapedAt { get; set; } = DateTime.UtcNow;
    }

    public class AppBatch
    {
        public List<AnnualProcurementPlan> Items { get; set; } = new();
        public int Page { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
        public bool HasMorePages { get; set; }
    }
}
