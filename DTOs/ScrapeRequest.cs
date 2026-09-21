namespace ZimbabweTenderAPI.DTOs
{
    public class ScrapeRequest
    {
        public int StartPage { get; set; } = 1;
        public int EndPage { get; set; } = 5;
        public bool SaveToDatabase { get; set; } = false;
        public string ExportFormat { get; set; } = "json"; // json, csv, xml
        public bool IncludeDetails { get; set; } = false;
        public List<string> FilterEntities { get; set; } = new();
        public List<string> FilterCategories { get; set; } = new();
        public DateTime? PublishDateFrom { get; set; }
        public DateTime? PublishDateTo { get; set; }
        public DateTime? ClosingDateFrom { get; set; }
        public DateTime? ClosingDateTo { get; set; }

        // New properties for searching
        public string SearchRefNo { get; set; }
        public string SearchtenderRefNo { get; set; }
        public string SearchNoticeTitle { get; set; }
        public string SearchSuppCode { get; set; }
        public string SearchSuppName { get; set; }
        public string SearchDept { get; set; }
        public string SearchAwardNoticeNo { get; set; }
        public string SearchAwardee { get; set; }
        public string SearchAwardTitle { get; set; }
        public string SearchLineItem { get; set; }
        public DateTime? AwardDateFrom { get; set; }
        public DateTime? AwardDateTo { get; set; }
        public string SearchAnnualYear { get; set; }


        // Add this new property for string matching search
        public string SearchString { get; set; }

        // Add this for controlling search behavior
        public bool UseAdvancedSearch { get; set; } = true;
        public int MaxPagesToSearch { get; set; } = 10;


      
        // Add these for procurement plan specific search
        public string SearchEntity { get; set; }
        public string SearchYear { get; set; }
        public string SearchBudgetCode { get; set; }
    }

}
