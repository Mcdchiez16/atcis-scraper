namespace ZimbabweTenderAPI.DTOs
{
    public class GeminiInsightRequest
    {
        public string Prompt { get; set; }
        public int? PageNumber { get; set; } = 1;
        public int? MaxTenders { get; set; } = 10;
        public string EntityFilter { get; set; }
        public string CategoryFilter { get; set; }
        public bool IncludeAnalysis { get; set; } = true;
        public bool IncludeRecommendations { get; set; } = true;
        public string OutputFormat { get; set; } = "html"; // html, json, markdown
    }

    public class GeminiInsightResponse
    {
        public string Analysis { get; set; }
        public List<TenderInsight> RelevantTenders { get; set; } = new List<TenderInsight>();
        public List<string> Recommendations { get; set; } = new List<string>();
        public double ConfidenceScore { get; set; }
        public string GeneratedAt { get; set; }
        public string Format { get; set; }
        public bool WasTruncated { get; set; }
    }

    public class TenderInsight
    {
        public string TenderId { get; set; }
        public string Title { get; set; }
        public string ReferenceNumber { get; set; }
        public string ProcuringEntity { get; set; }
        public string DetailsUrl { get; set; }
        public string RelevanceReason { get; set; }
        public double RelevanceScore { get; set; }
        public DateTime? ClosingDate { get; set; }
        public string RecommendedAction { get; set; }
        public List<string> MatchingCriteria { get; set; } = new List<string>();
    }

    public class GeminiConfig
    {
        public string ApiKey { get; set; }
        public string ModelName { get; set; } = "gemini-1.5-pro";
        public double Temperature { get; set; } = 0.2;
        public int MaxOutputTokens { get; set; } = 2000;
    }

    public class TenderDetail
    {
        public string TenderId { get; set; }
        public string Description { get; set; }
        public string Requirements { get; set; }
        public string EvaluationCriteria { get; set; }
        public string BudgetEstimate { get; set; }
        public string FullContent { get; set; }
        public DateTime ScrapedAt { get; set; }
        public string DetailsUrl { get; set; }
    }

    public class DetailedTenderAnalysisRequest : GeminiInsightRequest
    {
        public bool IncludeDetailContent { get; set; } = true;
        public int MaxTenderDetails { get; set; } = 5;
        public bool AnalyzeRequirements { get; set; } = true;
        public bool AnalyzeBudget { get; set; } = true;
    }
}