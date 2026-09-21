using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Data;
using ZimbabweTenderAPI.Data.Entities;
using ZimbabweTenderAPI.Models;

namespace ZimbabweTenderAPI.Services
{
    public interface ITenderAnalysisService
    {
        Task<TenderRecommendation> AnalyzeTenderForParticipation(string tenderId);
        Task<List<BusinessOpportunity>> IdentifyBusinessOpportunities(int days = 90);
        Task<TenderPatternAnalysis> AnalyzeTenderPatterns(string? categoryCode = null, int monthsBack = 6);
        Task<List<TenderAlert>> GenerateStakeholderAlerts();
    }

    public class TenderAnalysisService : ITenderAnalysisService
    {
        private readonly ApplicationDbContext _context;
        private readonly IGeminiAIService _geminiService;
        private readonly ILogger<TenderAnalysisService> _logger;

        public TenderAnalysisService(
            ApplicationDbContext context,
            IGeminiAIService geminiService,
            ILogger<TenderAnalysisService> logger)
        {
            _context = context;
            _geminiService = geminiService;
            _logger = logger;
        }

        public async Task<TenderRecommendation> AnalyzeTenderForParticipation(string tenderId)
        {
            try
            {
                // Get tender from live or closed
                var liveTender = await _context.LiveTenders
                    .FirstOrDefaultAsync(t => t.TenderId == tenderId);

                if (liveTender == null)
                {
                    return new TenderRecommendation
                    {
                        TenderId = tenderId,
                        ShouldParticipate = false,
                        ConfidenceScore = 0,
                        Reason = "Tender not found"
                    };
                }

                // Analyze using AI and historical data
                var historicalWins = await GetHistoricalPerformance(
                    JsonSerializer.Deserialize<List<string>>(liveTender.CategoryCodes ?? "[]") ?? new List<string>()
                );

                var recommendation = new TenderRecommendation
                {
                    TenderId = tenderId,
                    TenderTitle = liveTender.Title,
                    ProcuringEntity = liveTender.ProcuringEntity,
                    ClosingDate = liveTender.ClosingDate,
                    CategoryCodes = JsonSerializer.Deserialize<List<string>>(liveTender.CategoryCodes ?? "[]") ?? new List<string>(),
                    EstimatedValue = ExtractEstimatedValue(liveTender.Scope),
                    DaysUntilClosing = liveTender.ClosingDate.HasValue 
                        ? (int)(liveTender.ClosingDate.Value - DateTime.UtcNow).TotalDays 
                        : 0
                };

                // Calculate participation recommendation
                recommendation.ConfidenceScore = CalculateConfidenceScore(recommendation, historicalWins);
                recommendation.ShouldParticipate = recommendation.ConfidenceScore >= 60;
                recommendation.Reason = GenerateRecommendationReason(recommendation, historicalWins);

                // Identify preparation requirements
                recommendation.PreparationRequirements = await IdentifyPreparationRequirements(liveTender);

                // Find similar past tenders
                recommendation.SimilarTenders = await FindSimilarTenders(tenderId);

                // Identify competitors
                recommendation.LikelyCompetitors = await IdentifyCompetitors(recommendation.CategoryCodes ?? new List<string>());

                return recommendation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error analyzing tender {tenderId}");
                return new TenderRecommendation
                {
                    TenderId = tenderId,
                    ShouldParticipate = false,
                    ConfidenceScore = 0,
                    Reason = $"Analysis error: {ex.Message}"
                };
            }
        }

        public async Task<List<BusinessOpportunity>> IdentifyBusinessOpportunities(int days = 90)
        {
            var opportunities = new List<BusinessOpportunity>();
            var cutoffDate = DateTime.UtcNow.AddDays(-days);

            // Analyze recent awards to find supply chain opportunities
            var recentAwards = await _context.AwardNotices
                .Where(a => a.CreatedAt >= cutoffDate)
                .Include(a => a.ClosedTenders)
                .ToListAsync();

            foreach (var award in recentAwards)
            {
                var closedTender = award.ClosedTenders?.FirstOrDefault();
                if (closedTender == null) continue;

                var categoryNames = JsonSerializer.Deserialize<List<string>>(closedTender.CategoryNames ?? "[]") ?? new List<string>();

                // Identify opportunities based on award categories
                var foundOpps = IdentifySupplyChainOpportunities(
                    award.Awardee,
                    award.AwardTitle,
                    categoryNames,
                    award.ContractValue
                );

                // Add found opportunities
                opportunities.AddRange(foundOpps);
            }

            // Analyze procurement plans for future opportunities
            var procurementPlans = await _context.ProcurementPlans
                .Include(p => p.Items)
                .Where(p => p.Year == DateTime.Now.Year.ToString() || 
                           p.Year == (DateTime.Now.Year + 1).ToString())
                .ToListAsync();

            foreach (var plan in procurementPlans)
            {
                if (plan.Items == null || !plan.Items.Any()) continue;

                var planOpportunities = AnalyzeProcurementPlanForOpportunities(plan);
                opportunities.AddRange(planOpportunities);
            }

            return opportunities.OrderByDescending(o => o.ConfidenceScore).Take(50).ToList();
        }

        public async Task<TenderPatternAnalysis> AnalyzeTenderPatterns(string? categoryCode = null, int monthsBack = 6)
        {
            var cutoffDate = DateTime.UtcNow.AddMonths(-monthsBack);
            var analysis = new TenderPatternAnalysis
            {
                AnalysisDate = DateTime.UtcNow,
                PeriodMonths = monthsBack,
                CategoryFilter = categoryCode
            };

            // Get tenders for analysis
            var tenders = await _context.ClosedTenders
                .Where(t => t.CreatedAt >= cutoffDate)
                .ToListAsync();

            if (!string.IsNullOrEmpty(categoryCode))
            {
                tenders = tenders.Where(t =>
                {
                    var codes = JsonSerializer.Deserialize<List<string>>(t.CategoryCodes ?? "[]");
                    return codes?.Contains(categoryCode) == true;
                }).ToList();
            }

            // Analyze patterns
            analysis.TotalTenders = tenders.Count;
            analysis.ProcuringEntityDistribution = tenders
                .GroupBy(t => t.ProcuringEntity)
                .ToDictionary(g => g.Key, g => g.Count());

            analysis.CategoryDistribution = GetCategoryDistribution(tenders);
            analysis.MonthlyTrends = GetMonthlyTrends(tenders);
            analysis.AverageCompletionTime = CalculateAverageCompletionTime(tenders);
            analysis.PeakTenderingMonths = IdentifyPeakMonths(tenders);

            // Get awards for winners analysis
            var awardedTenderIds = tenders.Select(t => t.TenderId).ToList();
            var awards = await _context.AwardNotices
                .Where(a => awardedTenderIds.Contains(a.TenderId))
                .ToListAsync();

            analysis.TopWinners = awards
                .GroupBy(a => a.Awardee)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .ToDictionary(g => g.Key, g => g.Count());

            analysis.AverageTenderValue = awards.Average(a => a.ContractValue) ?? 0;
            analysis.TotalContractValue = awards.Sum(a => a.ContractValue) ?? 0;

            return analysis;
        }

        public async Task<List<TenderAlert>> GenerateStakeholderAlerts()
        {
            var alerts = new List<TenderAlert>();

            // New tenders in last 24 hours
            var newTenders = await _context.LiveTenders
                .Where(t => t.CreatedAt >= DateTime.UtcNow.AddHours(-24))
                .ToListAsync();

            foreach (var tender in newTenders)
            {
                alerts.Add(new TenderAlert
                {
                    AlertType = "NewTender",
                    Priority = "High",
                    TenderId = tender.TenderId,
                    Title = tender.Title,
                    Message = $"New tender available: {tender.Title}",
                    ProcuringEntity = tender.ProcuringEntity,
                    ClosingDate = tender.ClosingDate,
                    DaysUntilClosing = tender.ClosingDate.HasValue
                        ? (int)(tender.ClosingDate.Value - DateTime.UtcNow).TotalDays
                        : 0,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // Tenders closing soon (within 7 days)
            var closingSoon = await _context.LiveTenders
                .Where(t => t.ClosingDate.HasValue &&
                           t.ClosingDate >= DateTime.UtcNow &&
                           t.ClosingDate <= DateTime.UtcNow.AddDays(7))
                .ToListAsync();

            foreach (var tender in closingSoon)
            {
                var daysLeft = tender.ClosingDate.HasValue ? (int)(tender.ClosingDate.Value - DateTime.UtcNow).TotalDays : 0;
                alerts.Add(new TenderAlert
                {
                    AlertType = "ClosingSoon",
                    Priority = daysLeft <= 2 ? "Critical" : "Medium",
                    TenderId = tender.TenderId,
                    Title = tender.Title,
                    Message = $"Tender closing in {daysLeft} days: {tender.Title}",
                    ProcuringEntity = tender.ProcuringEntity,
                    ClosingDate = tender.ClosingDate,
                    DaysUntilClosing = daysLeft,
                    CreatedAt = DateTime.UtcNow
                });
            }

            // New awards in last 48 hours
            var recentAwards = await _context.AwardNotices
                .Where(a => a.CreatedAt >= DateTime.UtcNow.AddHours(-48))
                .ToListAsync();

            foreach (var award in recentAwards)
            {
                alerts.Add(new TenderAlert
                {
                    AlertType = "NewAward",
                    Priority = "Medium",
                    TenderId = award.TenderId,
                    Title = award.AwardTitle,
                    Message = $"Award notice published: {award.AwardTitle} awarded to {award.Awardee}",
                    Awardee = award.Awardee,
                    ContractValue = award.ContractValue,
                    CreatedAt = DateTime.UtcNow
                });
            }

            return alerts.OrderByDescending(a => a.Priority).ThenBy(a => a.DaysUntilClosing).ToList();
        }

        // Private helper methods
        private async Task<int> GetHistoricalPerformance(List<string> categories)
        {
            // Count how many similar tenders were won in the past
            var similarAwards = await _context.AwardNotices
                .Where(a => a.CreatedAt >= DateTime.UtcNow.AddMonths(-12))
                .ToListAsync();

            // In real implementation, filter by organization's name in Awardee field
            return similarAwards.Count;
        }

        private decimal CalculateConfidenceScore(TenderRecommendation rec, int historicalWins)
        {
            decimal score = 50; // Base score

            // Time factor
            if (rec.DaysUntilClosing > 14) score += 15;
            else if (rec.DaysUntilClosing > 7) score += 10;
            else if (rec.DaysUntilClosing < 3) score -= 20;

            // Historical performance
            if (historicalWins > 5) score += 20;
            else if (historicalWins > 2) score += 10;

            // Value factor
            if (rec.EstimatedValue > 0 && rec.EstimatedValue < 1000000) score += 10;

            return Math.Max(0, Math.Min(100, score));
        }

        private string GenerateRecommendationReason(TenderRecommendation rec, int historicalWins)
        {
            var reasons = new List<string>();

            if (rec.ConfidenceScore >= 70)
                reasons.Add("Strong alignment with organizational capabilities");
            else if (rec.ConfidenceScore >= 50)
                reasons.Add("Moderate alignment with organizational capabilities");
            else
                reasons.Add("Limited alignment with organizational capabilities");

            if (rec.DaysUntilClosing < 7)
                reasons.Add($"Short preparation time ({rec.DaysUntilClosing} days)");
            else
                reasons.Add($"Adequate preparation time ({rec.DaysUntilClosing} days)");

            if (historicalWins > 0)
                reasons.Add($"Previous success in similar tenders ({historicalWins} wins)");

            return string.Join(". ", reasons) + ".";
        }

        private async Task<List<string>> IdentifyPreparationRequirements(LiveTenderEntity tender)
        {
            var requirements = new List<string>();

            // Extract common requirements
            var scope = tender.Scope?.ToLower() ?? "";

            if (scope.Contains("tax") || scope.Contains("clearance"))
                requirements.Add("Tax clearance certificate");
            if (scope.Contains("registration") || scope.Contains("company"))
                requirements.Add("Company registration documents");
            if (scope.Contains("financial") || scope.Contains("statement"))
                requirements.Add("Financial statements (last 3 years)");
            if (scope.Contains("insurance"))
                requirements.Add("Insurance certificates");
            if (scope.Contains("experience") || scope.Contains("reference"))
                requirements.Add("Reference letters from previous clients");

            // Add standard requirements
            requirements.Add("Completed bid/proposal form");
            requirements.Add("Valid PRAZ registration");
            requirements.Add("Signed declaration forms");

            return requirements;
        }

        private async Task<List<SimilarTenderInfo>> FindSimilarTenders(string tenderId)
        {
            var currentTender = await _context.LiveTenders
                .FirstOrDefaultAsync(t => t.TenderId == tenderId);

            if (currentTender == null) return new List<SimilarTenderInfo>();

            var categories = JsonSerializer.Deserialize<List<string>>(currentTender.CategoryCodes ?? "[]");

            var similarTenders = await _context.ClosedTenders
                .Where(t => t.CreatedAt >= DateTime.UtcNow.AddMonths(-12))
                .ToListAsync();

            return similarTenders
                .Where(t =>
                {
                    var tCats = JsonSerializer.Deserialize<List<string>>(t.CategoryCodes ?? "[]");
                    return tCats?.Any(c => categories?.Contains(c) == true) == true;
                })
                .Take(5)
                .Select(t => new SimilarTenderInfo
                {
                    TenderId = t.TenderId,
                    Title = t.Title,
                    ProcuringEntity = t.ProcuringEntity,
                    Outcome = "Closed"
                })
                .ToList();
        }

        private async Task<List<string>> IdentifyCompetitors(List<string> categoryCs)
        {
            var competitors = await _context.AwardNotices
                .Where(a => a.CreatedAt >= DateTime.UtcNow.AddMonths(-12))
                .Select(a => a.Awardee)
                .Distinct()
                .Take(10)
                .ToListAsync();

            return competitors;
        }

        private decimal ExtractEstimatedValue(string scope)
        {
            // Simple extraction - in production, use regex and NLP
            if (string.IsNullOrEmpty(scope)) return 0;

            // Look for USD, ZWL amounts
            var words = scope.Split(' ');
            foreach (var word in words)
            {
                if (decimal.TryParse(word.Replace(",", ""), out decimal value))
                {
                    if (value > 1000) return value;
                }
            }
            return 0;
        }

        private List<BusinessOpportunity> IdentifySupplyChainOpportunities(
            string winner, string projectTitle, List<string> categories, decimal? value)
        {
            var opportunities = new List<BusinessOpportunity>();

            // Example: Construction tender won by Company A might need suppliers
            if (categories?.Any(c => c.Contains("45") || c.Contains("Construction")) == true)
            {
                opportunities.Add(new BusinessOpportunity
                {
                    Type = "SupplyChain",
                    Description = $"Supply building materials to {winner} for project: {projectTitle}",
                    PotentialClient = winner,
                    EstimatedValue = value * 0.3m ?? 0, // 30% of main contract
                    ConfidenceScore = 65,
                    ActionRequired = $"Contact {winner} to offer building materials supply",
                    Timeline = "Immediate - 3 months"
                });
            }

            return opportunities;
        }

        private List<BusinessOpportunity> AnalyzeProcurementPlanForOpportunities(ProcurementPlanEntity plan)
        {
            var opportunities = new List<BusinessOpportunity>();

            if (plan.Items == null) return opportunities;

            foreach (var item in plan.Items)
            {
                opportunities.Add(new BusinessOpportunity
                {
                    Type = "FutureTender",
                    Description = $"Upcoming procurement: {item.Description} by {plan.ProcuringEntity}",
                    PotentialClient = plan.ProcuringEntity,
                    EstimatedValue = 0, // Estimate not available in current entity
                    ConfidenceScore = 70,
                    ActionRequired = "Prepare tender documents and capabilities statement",
                    Timeline = $"Expected in {plan.Year}",
                    SourceAwardId = null,
                    SourceTenderId = item.ItemId ?? "",
                    IdentifiedDate = DateTime.UtcNow
                });
            }

            return opportunities.Take(5).ToList();
        }

        private Dictionary<string, int> GetCategoryDistribution(List<ClosedTenderEntity> tenders)
        {
            var distribution = new Dictionary<string, int>();

            foreach (var tender in tenders)
            {
                var categories = JsonSerializer.Deserialize<List<string>>(tender.CategoryNames ?? "[]");
                foreach (var category in categories ?? new List<string>())
                {
                    if (distribution.ContainsKey(category))
                        distribution[category]++;
                    else
                        distribution[category] = 1;
                }
            }

            return distribution.OrderByDescending(kvp => kvp.Value).Take(10).ToDictionary(k => k.Key, v => v.Value);
        }

        private Dictionary<string, int> GetMonthlyTrends(List<ClosedTenderEntity> tenders)
        {
            return tenders
                .GroupBy(t => t.PublishDate?.ToString("yyyy-MM") ?? "Unknown")
                .ToDictionary(g => g.Key, g => g.Count());
        }

        private double CalculateAverageCompletionTime(List<ClosedTenderEntity> tenders)
        {
            var withBothDates = tenders
                .Where(t => t.PublishDate.HasValue && t.ClosingDate.HasValue)
                .ToList();

            if (!withBothDates.Any()) return 0;

            return withBothDates.Average(t =>
                t.ClosingDate.HasValue && t.PublishDate.HasValue 
                    ? (t.ClosingDate.Value - t.PublishDate.Value).TotalDays 
                    : 0);
        }

        private List<string> IdentifyPeakMonths(List<ClosedTenderEntity> tenders)
        {
            return tenders
                .Where(t => t.PublishDate.HasValue)
                .GroupBy(t => t.PublishDate!.Value.Month)
                .OrderByDescending(g => g.Count())
                .Take(3)
                .Select(g => new DateTime(2000, g.Key, 1).ToString("MMMM"))
                .ToList();
        }
    }

    // DTOs for Analysis
    public class TenderRecommendation
    {
        public string TenderId { get; set; } = string.Empty;
        public string TenderTitle { get; set; } = string.Empty;
        public string ProcuringEntity { get; set; } = string.Empty;
        public DateTime? ClosingDate { get; set; }
        public int DaysUntilClosing { get; set; }
        public List<string> CategoryCodes { get; set; } = new();
        public decimal EstimatedValue { get; set; }
        public bool ShouldParticipate { get; set; }
        public decimal ConfidenceScore { get; set; }
        public string Reason { get; set; } = string.Empty;
        public List<string> PreparationRequirements { get; set; } = new();
        public List<SimilarTenderInfo> SimilarTenders { get; set; } = new();
        public List<string> LikelyCompetitors { get; set; } = new();
    }

    public class SimilarTenderInfo
    {
        public string TenderId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string ProcuringEntity { get; set; } = string.Empty;
        public string Outcome { get; set; } = string.Empty;
    }

    public class BusinessOpportunity
    {
        public string Type { get; set; } = string.Empty; // SupplyChain, FutureTender, Partnership
        public string Description { get; set; } = string.Empty;
        public string PotentialClient { get; set; } = string.Empty;
        public decimal EstimatedValue { get; set; }
        public decimal ConfidenceScore { get; set; }
        public string ActionRequired { get; set; } = string.Empty;
        public string Timeline { get; set; } = string.Empty;
        public int? SourceAwardId { get; set; }
        public string SourceTenderId { get; set; } = string.Empty;
        public DateTime IdentifiedDate { get; set; }
    }

    public class TenderPatternAnalysis
    {
        public DateTime AnalysisDate { get; set; }
        public int PeriodMonths { get; set; }
        public string? CategoryFilter { get; set; }
        public int TotalTenders { get; set; }
        public Dictionary<string, int> ProcuringEntityDistribution { get; set; } = new();
        public Dictionary<string, int> CategoryDistribution { get; set; } = new();
        public Dictionary<string, int> MonthlyTrends { get; set; } = new();
        public Dictionary<string, int> TopWinners { get; set; } = new();
        public decimal AverageTenderValue { get; set; }
        public decimal TotalContractValue { get; set; }
        public double AverageCompletionTime { get; set; }
        public List<string> PeakTenderingMonths { get; set; } = new();
    }

    public class TenderAlert
    {
        public string AlertType { get; set; } = string.Empty; // NewTender, ClosingSoon, NewAward
        public string Priority { get; set; } = string.Empty; // Critical, High, Medium, Low
        public string TenderId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string ProcuringEntity { get; set; } = string.Empty;
        public DateTime? ClosingDate { get; set; }
        public int DaysUntilClosing { get; set; }
        public string Awardee { get; set; } = string.Empty;
        public decimal? ContractValue { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
