using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using ZimbabweTenderAPI.DTOs;

namespace ZimbabweTenderAPI.Services
{
    public interface IStringMatchingService
    {
        double CalculateLevenshteinDistance(string s1, string s2);
        double CalculateJaroWinklerDistance(string s1, string s2);
        double CalculateCosineSimilarity(string s1, string s2);
        double CalculateFuzzyMatchScore(string searchTerm, string target, List<string> additionalKeywords = null);
        List<AnnualProcurementPlanDto> FindBestMatches(List<AnnualProcurementPlanDto> allPlans, string searchTerm, int topN = 5);
    }
    public class StringMatchingService : IStringMatchingService
    {
        private readonly ILogger<StringMatchingService> _logger;

        public StringMatchingService(ILogger<StringMatchingService> logger)
        {
            _logger = logger;
        }

        public double CalculateLevenshteinDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1))
                return string.IsNullOrEmpty(s2) ? 0 : s2.Length;

            if (string.IsNullOrEmpty(s2))
                return s1.Length;

            s1 = s1.ToLower();
            s2 = s2.ToLower();

            int n = s1.Length;
            int m = s2.Length;
            var d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; d[i, 0] = i++) { }
            for (int j = 0; j <= m; d[0, j] = j++) { }

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            double maxLength = Math.Max(s1.Length, s2.Length);
            if (maxLength == 0) return 1.0;

            return 1.0 - (d[n, m] / maxLength);
        }

        public double CalculateJaroWinklerDistance(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;

            s1 = s1.ToLower();
            s2 = s2.ToLower();

            // Jaro distance
            int matchDistance = Math.Max(s1.Length, s2.Length) / 2 - 1;
            var s1Matches = new bool[s1.Length];
            var s2Matches = new bool[s2.Length];

            int matches = 0;
            int transpositions = 0;

            for (int i = 0; i < s1.Length; i++)
            {
                int start = Math.Max(0, i - matchDistance);
                int end = Math.Min(i + matchDistance + 1, s2.Length);

                for (int j = start; j < end; j++)
                {
                    if (!s2Matches[j] && s1[i] == s2[j])
                    {
                        s1Matches[i] = true;
                        s2Matches[j] = true;
                        matches++;
                        break;
                    }
                }
            }

            if (matches == 0) return 0;

            int k = 0;
            for (int i = 0; i < s1.Length; i++)
            {
                if (s1Matches[i])
                {
                    while (!s2Matches[k]) k++;
                    if (s1[i] != s2[k]) transpositions++;
                    k++;
                }
            }

            double jaro = ((double)matches / s1.Length +
                          (double)matches / s2.Length +
                          (double)(matches - transpositions / 2.0) / matches) / 3.0;

            // Winkler modification
            int prefixLength = 0;
            int maxPrefixLength = Math.Min(4, Math.Min(s1.Length, s2.Length));
            for (int i = 0; i < maxPrefixLength; i++)
            {
                if (s1[i] == s2[i]) prefixLength++;
                else break;
            }

            return jaro + (prefixLength * 0.1 * (1 - jaro));
        }

        public double CalculateCosineSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;

            var tokens1 = Tokenize(s1);
            var tokens2 = Tokenize(s2);

            if (tokens1.Count == 0 || tokens2.Count == 0)
                return 0;

            var allTokens = tokens1.Union(tokens2).Distinct().ToList();
            var vector1 = new double[allTokens.Count];
            var vector2 = new double[allTokens.Count];

            for (int i = 0; i < allTokens.Count; i++)
            {
                vector1[i] = tokens1.Count(t => t == allTokens[i]);
                vector2[i] = tokens2.Count(t => t == allTokens[i]);
            }

            double dotProduct = 0;
            double magnitude1 = 0;
            double magnitude2 = 0;

            for (int i = 0; i < allTokens.Count; i++)
            {
                dotProduct += vector1[i] * vector2[i];
                magnitude1 += vector1[i] * vector1[i];
                magnitude2 += vector2[i] * vector2[i];
            }

            magnitude1 = Math.Sqrt(magnitude1);
            magnitude2 = Math.Sqrt(magnitude2);

            if (magnitude1 == 0 || magnitude2 == 0)
                return 0;

            return dotProduct / (magnitude1 * magnitude2);
        }

        private List<string> Tokenize(string text)
        {
            return Regex.Replace(text.ToLower(), @"[^\w\s]", "")
                       .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                       .Where(t => t.Length > 2) // Ignore very short words
                       .ToList();
        }

        public double CalculateFuzzyMatchScore(string searchTerm, string target, List<string> additionalKeywords = null)
        {
            if (string.IsNullOrWhiteSpace(searchTerm) || string.IsNullOrWhiteSpace(target))
                return 0;

            // Normalize strings
            searchTerm = searchTerm.ToLower().Trim();
            target = target.ToLower().Trim();

            // Check for exact match
            if (target.Contains(searchTerm))
                return 1.0;

            // Check for partial matches
            var searchWords = searchTerm.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var targetWords = target.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            double wordMatchScore = 0;
            foreach (var searchWord in searchWords)
            {
                if (target.Contains(searchWord))
                {
                    wordMatchScore += 1.0 / searchWords.Length;
                }
                else
                {
                    // Check for similar words using Levenshtein
                    foreach (var targetWord in targetWords)
                    {
                        if (searchWord.Length > 3 && targetWord.Length > 3)
                        {
                            double similarity = CalculateLevenshteinDistance(searchWord, targetWord);
                            if (similarity > 0.8)
                            {
                                wordMatchScore += similarity / searchWords.Length;
                                break;
                            }
                        }
                    }
                }
            }

            // Calculate different similarity metrics
            double levenshteinScore = CalculateLevenshteinDistance(searchTerm, target);
            double jaroWinklerScore = CalculateJaroWinklerDistance(searchTerm, target);
            double cosineScore = CalculateCosineSimilarity(searchTerm, target);

            // Weighted combination of scores
            double finalScore = (wordMatchScore * 0.4) +
                               (jaroWinklerScore * 0.3) +
                               (cosineScore * 0.2) +
                               (levenshteinScore * 0.1);

            // Boost score if additional keywords are provided and found
            if (additionalKeywords != null && additionalKeywords.Any())
            {
                foreach (var keyword in additionalKeywords)
                {
                    if (target.Contains(keyword.ToLower()))
                    {
                        finalScore += 0.1;
                        break;
                    }
                }
            }

            return Math.Min(finalScore, 1.0);
        }

        public List<AnnualProcurementPlanDto> FindBestMatches(
            List<AnnualProcurementPlanDto> allPlans,
            string searchTerm,
            int topN = 5)
        {
            if (allPlans == null || !allPlans.Any())
                return new List<AnnualProcurementPlanDto>();

            var scoredPlans = allPlans.Select(plan =>
            {
                // Calculate scores for both entity and year
                double entityScore = CalculateFuzzyMatchScore(searchTerm, plan.ProcuringEntity);
                double yearScore = CalculateFuzzyMatchScore(searchTerm, plan.Year);

                // Combine scores with weights
                double combinedScore = (entityScore * 0.8) + (yearScore * 0.2);

                return new
                {
                    Plan = plan,
                    Score = combinedScore,
                    EntityScore = entityScore,
                    YearScore = yearScore
                };
            })
            .Where(x => x.Score > 0.3) // Minimum threshold
            .OrderByDescending(x => x.Score)
            .Take(topN)
            .ToList();

            _logger.LogInformation($"Found {scoredPlans.Count} matches for '{searchTerm}' with minimum score 0.3");

            return scoredPlans.Select(x => x.Plan).ToList();
        }
    }
}