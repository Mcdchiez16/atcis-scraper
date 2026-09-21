using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Models;

namespace ZimbabweTenderAPI.Services
{
    public class TenderSimilarityService
    {
        private readonly ITenderScraperService _scraperService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<TenderSimilarityService> _logger;
        private readonly HttpClient _httpClient;  // Add this field
        private const int MaxPagesToSearch = 10;

        // Update constructor to accept 4 arguments
        public TenderSimilarityService(
            ITenderScraperService scraperService,
            IMemoryCache cache,
            ILogger<TenderSimilarityService> logger,
            HttpClient httpClient)  // Add this parameter
        {
            _scraperService = scraperService;
            _cache = cache;
            _logger = logger;
            _httpClient = httpClient;  // Initialize the new field
        }

        public async Task<List<TenderSimilarityDto>> AnalyzeTenderSimilarityAsync(string tenderId)
        {
            try
            {
                _logger.LogInformation($"Analyzing similarity for tender {tenderId}");

                // Get the target tender
                var targetTender = await FindTenderByIdAsync(tenderId);
                if (targetTender == null)
                {
                    _logger.LogWarning($"Tender {tenderId} not found");
                    return new List<TenderSimilarityDto>();
                }

                // Get a collection of tenders to compare against
                var comparisonTenders = await GetTendersForComparisonAsync();

                // Calculate similarity scores using probabilistic algorithms
                var similarities = new List<TenderSimilarityDto>();

                foreach (var tender in comparisonTenders)
                {
                    if (tender.TenderId == targetTender.TenderId)
                        continue;

                    var similarityScore = CalculateOverallSimilarity(targetTender, tender);

                    if (similarityScore > 0.3) // Threshold for relevant similarity
                    {
                        similarities.Add(new TenderSimilarityDto
                        {
                            TenderId = tender.TenderId,
                            Title = tender.Title,
                            ReferenceNumber = tender.ReferenceNumber,
                            ProcuringEntity = tender.ProcuringEntity,
                            SimilarityScore = similarityScore,
                            MatchReasons = GetMatchReasons(targetTender, tender, similarityScore),
                            TargetTenderId = targetTender.TenderId,
                            TargetTitle = targetTender.Title,
                            CategorySimilarity = CalculateCategorySimilarity(targetTender, tender),
                            EntitySimilarity = CalculateEntitySimilarity(targetTender, tender),
                            TextSimilarity = CalculateTextSimilarityScore(targetTender, tender)
                        });
                    }
                }

                return similarities
                    .OrderByDescending(s => s.SimilarityScore)
                    .Take(10)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error analyzing similarity for tender {tenderId}");
                throw;
            }
        }

        private async Task<Tender> FindTenderByIdAsync(string tenderId)
        {
            // Search in current tenders
            for (int page = 1; page <= 5; page++)
            {
                try
                {
                    var batch = await _scraperService.ScrapePageAsync(page);
                    var tender = batch.Tenders.FirstOrDefault(t =>
                        t.TenderId?.Equals(tenderId, StringComparison.OrdinalIgnoreCase) == true ||
                        t.ReferenceNumber?.Equals(tenderId, StringComparison.OrdinalIgnoreCase) == true);

                    if (tender != null)
                        return tender;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error searching page {page} for tender {tenderId}");
                }
            }

            // Search in past tenders
            try
            {
                var pastRequest = new ScrapeRequest
                {
                    SearchString = tenderId,
                    StartPage = 1,
                    MaxPagesToSearch = 3
                };

                var pastTenders = await _scraperService.SearchPastTendersAsync(pastRequest);
                return pastTenders.Items.FirstOrDefault(t =>
                    t.TenderId?.Equals(tenderId, StringComparison.OrdinalIgnoreCase) == true ||
                    t.ReferenceNumber?.Equals(tenderId, StringComparison.OrdinalIgnoreCase) == true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error searching past tenders for {tenderId}");
                return null;
            }
        }



        private async Task<List<Tender>> GetTendersForComparisonAsync()
        {
            var allTenders = new List<Tender>();

            // Get current tenders
            for (int page = 1; page <= 3; page++)
            {
                try
                {
                    var batch = await _scraperService.ScrapePageAsync(page);
                    allTenders.AddRange(batch.Tenders);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error getting comparison tenders from page {page}");
                }
            }

            // Get some past tenders
            try
            {
                var pastRequest = new ScrapeRequest
                {
                    SearchString = "",
                    StartPage = 1,
                    MaxPagesToSearch = 2
                };

                var pastTenders = await _scraperService.SearchPastTendersAsync(pastRequest);
                allTenders.AddRange(pastTenders.Items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting past tenders for comparison");
            }

            return allTenders.DistinctBy(t => t.TenderId).ToList();
        }

        private double CalculateOverallSimilarity(Tender tender1, Tender tender2)
        {
            // Weighted combination of different similarity measures
            var weights = new Dictionary<string, double>
            {
                { "text", 0.4 },      // Text content similarity
                { "title", 0.3 },     // Title similarity
                { "entity", 0.15 },   // Entity similarity
                { "category", 0.15 }  // Category similarity
            };

            var textSimilarity = CalculateTextSimilarityScore(tender1, tender2);
            var titleSimilarity = CalculateTitleSimilarity(tender1.Title, tender2.Title);
            var entitySimilarity = CalculateEntitySimilarity(tender1, tender2);
            var categorySimilarity = CalculateCategorySimilarity(tender1, tender2);

            return (textSimilarity * weights["text"] +
                    titleSimilarity * weights["title"] +
                    entitySimilarity * weights["entity"] +
                    categorySimilarity * weights["category"]);
        }

        private double CalculateTextSimilarityScore(Tender tender1, Tender tender2)
        {
            // Combine all text fields
            var text1 = CombineTenderText(tender1);
            var text2 = CombineTenderText(tender2);

            // Use multiple string matching algorithms
            var jaroWinkler = CalculateJaroWinklerSimilarity(text1, text2);
            var cosine = CalculateCosineSimilarity(text1, text2);
            var levenshtein = CalculateNormalizedLevenshteinSimilarity(text1, text2);

            // Weighted average of different algorithms
            return (jaroWinkler * 0.4 + cosine * 0.3 + levenshtein * 0.3);
        }

        private string CombineTenderText(Tender tender)
        {
            var parts = new List<string>();

            if (!string.IsNullOrEmpty(tender.Title))
                parts.Add(tender.Title);

            if (!string.IsNullOrEmpty(tender.Scope))
                parts.Add(tender.Scope);

            if (!string.IsNullOrEmpty(tender.ProcuringEntity))
                parts.Add(tender.ProcuringEntity);

            if (tender.CategoryNames?.Any() == true)
                parts.Add(string.Join(" ", tender.CategoryNames));

            return string.Join(" ", parts).ToLowerInvariant();
        }

        // 1. Jaro-Winkler Similarity (good for short strings)
        private double CalculateJaroWinklerSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;

            var jaroDistance = CalculateJaroDistance(s1, s2);

            // Winkler modification: boost similarity for strings with common prefix
            int prefixLength = 0;
            int maxPrefixLength = Math.Min(4, Math.Min(s1.Length, s2.Length));

            for (int i = 0; i < maxPrefixLength; i++)
            {
                if (s1[i] == s2[i])
                    prefixLength++;
                else
                    break;
            }

            return jaroDistance + (prefixLength * 0.1 * (1 - jaroDistance));
        }

        private double CalculateJaroDistance(string s1, string s2)
        {
            if (s1 == s2) return 1.0;

            int len1 = s1.Length;
            int len2 = s2.Length;

            if (len1 == 0 || len2 == 0) return 0.0;

            int matchDistance = Math.Max(len1, len2) / 2 - 1;

            var matches1 = new bool[len1];
            var matches2 = new bool[len2];

            int matches = 0;
            int transpositions = 0;

            for (int i = 0; i < len1; i++)
            {
                int start = Math.Max(0, i - matchDistance);
                int end = Math.Min(i + matchDistance + 1, len2);

                for (int j = start; j < end; j++)
                {
                    if (!matches2[j] && s1[i] == s2[j])
                    {
                        matches1[i] = true;
                        matches2[j] = true;
                        matches++;
                        break;
                    }
                }
            }

            if (matches == 0) return 0.0;

            int k = 0;
            for (int i = 0; i < len1; i++)
            {
                if (matches1[i])
                {
                    while (!matches2[k]) k++;
                    if (s1[i] != s2[k]) transpositions++;
                    k++;
                }
            }

            double m = matches;
            return ((m / len1) + (m / len2) + ((m - transpositions / 2.0) / m)) / 3.0;
        }

        // 2. Cosine Similarity (good for comparing text content)
        private double CalculateCosineSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;

            var vector1 = CreateTermFrequencyVector(s1);
            var vector2 = CreateTermFrequencyVector(s2);

            var allTerms = vector1.Keys.Union(vector2.Keys).Distinct().ToList();

            double dotProduct = 0;
            double magnitude1 = 0;
            double magnitude2 = 0;

            foreach (var term in allTerms)
            {
                vector1.TryGetValue(term, out double freq1);
                vector2.TryGetValue(term, out double freq2);

                dotProduct += freq1 * freq2;
                magnitude1 += freq1 * freq1;
                magnitude2 += freq2 * freq2;
            }

            if (magnitude1 == 0 || magnitude2 == 0)
                return 0;

            return dotProduct / (Math.Sqrt(magnitude1) * Math.Sqrt(magnitude2));
        }

        private Dictionary<string, double> CreateTermFrequencyVector(string text)
        {
            var words = text.Split(new[] { ' ', ',', '.', '!', '?', ';', ':', '-', '(', ')', '[', ']', '{', '}' },
                                  StringSplitOptions.RemoveEmptyEntries);

            var vector = new Dictionary<string, double>();
            foreach (var word in words)
            {
                var normalized = word.ToLowerInvariant();
                if (vector.ContainsKey(normalized))
                    vector[normalized]++;
                else
                    vector[normalized] = 1;
            }

            // Normalize
            var maxFreq = vector.Values.Max();
            foreach (var key in vector.Keys.ToList())
            {
                vector[key] /= maxFreq;
            }

            return vector;
        }

        // 3. Normalized Levenshtein Distance
        private double CalculateNormalizedLevenshteinSimilarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
                return 0;

            int distance = CalculateLevenshteinDistance(s1, s2);
            int maxLength = Math.Max(s1.Length, s2.Length);

            if (maxLength == 0) return 1.0;

            return 1.0 - (double)distance / maxLength;
        }

        private int CalculateLevenshteinDistance(string a, string b)
        {
            if (string.IsNullOrEmpty(a))
                return string.IsNullOrEmpty(b) ? 0 : b.Length;
            if (string.IsNullOrEmpty(b))
                return a.Length;

            var matrix = new int[a.Length + 1, b.Length + 1];

            for (int i = 0; i <= a.Length; i++)
                matrix[i, 0] = i;
            for (int j = 0; j <= b.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;

                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            return matrix[a.Length, b.Length];
        }

        // 4. Title-specific similarity (using character n-grams)
        private double CalculateTitleSimilarity(string title1, string title2)
        {
            if (string.IsNullOrEmpty(title1) || string.IsNullOrEmpty(title2))
                return 0;

            var ngrams1 = GenerateCharacterNgrams(title1.ToLowerInvariant(), 3);
            var ngrams2 = GenerateCharacterNgrams(title2.ToLowerInvariant(), 3);

            var common = ngrams1.Intersect(ngrams2).Count();
            var total = ngrams1.Count + ngrams2.Count;

            if (total == 0) return 0;

            return (2.0 * common) / total;
        }

        private List<string> GenerateCharacterNgrams(string text, int n)
        {
            var ngrams = new List<string>();
            for (int i = 0; i <= text.Length - n; i++)
            {
                ngrams.Add(text.Substring(i, n));
            }
            return ngrams;
        }

        // 5. Entity similarity
        private double CalculateEntitySimilarity(Tender tender1, Tender tender2)
        {
            if (string.IsNullOrEmpty(tender1.ProcuringEntity) ||
                string.IsNullOrEmpty(tender2.ProcuringEntity))
                return 0;

            return CalculateJaroWinklerSimilarity(
                tender1.ProcuringEntity.ToLowerInvariant(),
                tender2.ProcuringEntity.ToLowerInvariant());
        }

        // 6. Category similarity
        private double CalculateCategorySimilarity(Tender tender1, Tender tender2)
        {
            if (tender1.CategoryNames == null || tender2.CategoryNames == null)
                return 0;

            var cats1 = tender1.CategoryNames.Select(c => c.ToLowerInvariant()).ToList();
            var cats2 = tender2.CategoryNames.Select(c => c.ToLowerInvariant()).ToList();

            var common = cats1.Intersect(cats2).Count();
            var total = cats1.Count + cats2.Count;

            if (total == 0) return 0;

            return (2.0 * common) / total;
        }

        private List<string> GetMatchReasons(Tender targetTender, Tender otherTender, double similarityScore)
        {
            var reasons = new List<string>();

            // Title similarity
            var titleSim = CalculateTitleSimilarity(targetTender.Title, otherTender.Title);
            if (titleSim > 0.6)
                reasons.Add($"Title similarity: {Math.Round(titleSim * 100)}%");

            // Entity match
            if (!string.IsNullOrEmpty(targetTender.ProcuringEntity) &&
                !string.IsNullOrEmpty(otherTender.ProcuringEntity) &&
                CalculateEntitySimilarity(targetTender, otherTender) > 0.7)
            {
                reasons.Add($"Same/similar procuring entity");
            }

            // Category match
            var catSim = CalculateCategorySimilarity(targetTender, otherTender);
            if (catSim > 0.5)
                reasons.Add($"Shared categories");

            // Text content similarity
            var textSim = CalculateTextSimilarityScore(targetTender, otherTender);
            if (textSim > 0.4)
                reasons.Add($"Similar content/scope");

            // Overall high similarity
            if (similarityScore > 0.7)
                reasons.Insert(0, "Highly similar overall");

            return reasons.Take(3).ToList();
        }

        // Additional methods for keyword-based search
        public async Task<List<TenderSimilarityDto>> FindSimilarTendersAsync(string title, int maxResults = 10)
        {
            try
            {
                _logger.LogInformation($"Finding similar tenders for: {title}");

                var allTenders = await GetTendersForComparisonAsync();
                var similarities = new List<TenderSimilarityDto>();

                foreach (var tender in allTenders)
                {
                    if (string.IsNullOrEmpty(tender.Title))
                        continue;

                    var titleSimilarity = CalculateTitleSimilarity(title, tender.Title);
                    var textSimilarity = CalculateJaroWinklerSimilarity(
                        title.ToLowerInvariant(),
                        tender.Title.ToLowerInvariant());

                    var combinedScore = (titleSimilarity * 0.7 + textSimilarity * 0.3);

                    if (combinedScore > 0.3)
                    {
                        similarities.Add(new TenderSimilarityDto
                        {
                            TenderId = tender.TenderId,
                            Title = tender.Title,
                            ReferenceNumber = tender.ReferenceNumber,
                            ProcuringEntity = tender.ProcuringEntity,
                            SimilarityScore = combinedScore,
                            MatchReasons = new List<string>
                            {
                                $"Title similarity: {Math.Round(combinedScore * 100)}%"
                            },
                            TargetTitle = title,
                            TextSimilarity = textSimilarity,
                            CategorySimilarity = 0,
                            EntitySimilarity = 0
                        });
                    }
                }

                return similarities
                    .OrderByDescending(s => s.SimilarityScore)
                    .Take(maxResults)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error finding similar tenders for: {title}");
                throw;
            }
        }


        /*public async Task<List<TenderParticipationDto>> GetCompanyParticipationAsync(string companyName)
        {
            try
            {
                _logger.LogInformation($"Getting participation for company: {companyName}");

                var participation = new List<TenderParticipationDto>();

                // Search in current tenders
                for (int page = 1; page <= 5; page++)
                {
                    try
                    {
                        var batch = await _scraperService.ScrapePageAsync(page);
                        var companyTenders = batch.Tenders.Where(t =>
                            (!string.IsNullOrEmpty(t.ProcuringEntity) &&
                             t.ProcuringEntity.IndexOf(companyName, StringComparison.OrdinalIgnoreCase) >= 0) ||
                            (!string.IsNullOrEmpty(t.Title) &&
                             t.Title.IndexOf(companyName, StringComparison.OrdinalIgnoreCase) >= 0))
                            .ToList();

                        foreach (var tender in companyTenders)
                        {
                            participation.Add(new TenderParticipationDto
                            {
                                TenderId = tender.TenderId,
                                Title = tender.Title,
                                ReferenceNumber = tender.ReferenceNumber,
                                ProcuringEntity = tender.ProcuringEntity,
                                CompanyName = companyName,
                                Role = "Procuring Entity",
                                PublishDate = tender.PublishDate,
                                ClosingDate = tender.ClosingDate,
                                Status = tender.ClosingDate > DateTime.UtcNow ? "Active" : "Closed"
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error scraping page {page} for company {companyName}");
                    }
                }

                return participation
                    .OrderByDescending(p => p.PublishDate ?? DateTime.MinValue)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting participation for company {companyName}");
                throw;
            }
        }*/

        public async Task<List<TenderParticipationDto>> GetCompanyParticipationAsync(string companyName)
        {
            try
            {
                _logger.LogInformation($"Getting participation for company: {companyName}");

                var allMatches = new Dictionary<Tender, double>();

                // Search in current tenders with fuzzy matching
                for (int page = 1; page <= 10; page++) // Search more pages
                {
                    try
                    {
                        var batch = await _scraperService.ScrapePageAsync(page);

                        foreach (var tender in batch.Tenders)
                        {
                            if (!string.IsNullOrEmpty(tender.ProcuringEntity))
                            {
                                var similarity = CalculateEnhancedCompanySimilarity(tender.ProcuringEntity, companyName);

                                if (similarity >= 0.6) // Lower threshold to catch more matches
                                {
                                    allMatches[tender] = similarity;
                                }
                            }
                        }

                        await Task.Delay(200); // Be respectful to the server
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error scraping page {page} for company {companyName}");
                    }
                }

                // Convert to DTOs
                var participation = allMatches
                    .OrderByDescending(kv => kv.Value) // Sort by similarity score
                    .Select(kv =>
                    {
                        var tender = kv.Key;
                        var similarity = kv.Value;

                        return new TenderParticipationDto
                        {
                            TenderId = tender.TenderId,
                            Title = tender.Title,
                            ReferenceNumber = tender.ReferenceNumber,
                            ProcuringEntity = tender.ProcuringEntity,
                            CompanyName = companyName,
                            Role = "Procuring Entity",
                            PublishDate = tender.PublishDate,
                            ClosingDate = tender.ClosingDate,
                            Status = tender.ClosingDate > DateTime.UtcNow ? "Active" : "Closed",
                            SimilarityScore = similarity
                        };
                    })
                    .ToList();

                return participation;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting participation for company {companyName}");
                throw;
            }
        }

        // Enhanced similarity calculation
        private double CalculateEnhancedCompanySimilarity(string name1, string name2)
        {
            if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
                return 0;

            var normalized1 = NormalizeCompanyNameEnhanced(name1);
            var normalized2 = NormalizeCompanyNameEnhanced(name2);

            // Calculate multiple similarity scores
            var scores = new List<double>
    {
        // Weighted combination
        CalculateJaroWinklerSimilarity(normalized1, normalized2) * 0.4,
        CalculateNormalizedLevenshteinSimilarity(normalized1, normalized2) * 0.3,
        CalculateCosineSimilarity(normalized1, normalized2) * 0.2,
        CalculatePartialMatchScore(normalized1, normalized2) * 0.1
    };

            // Bonus for common keywords
            var commonWords = GetCommonWords(name1, name2);
            if (commonWords.Count >= 2)
                scores.Add(0.1 * commonWords.Count);

            return scores.Sum();
        }

        private string NormalizeCompanyNameEnhanced(string companyName)
        {
            if (string.IsNullOrEmpty(companyName))
                return companyName;

            var normalized = companyName.ToLowerInvariant();

            // Remove punctuation and extra spaces
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");
            normalized = Regex.Replace(normalized, @"\s+", " ");

            // Remove common corporate suffixes
            var suffixes = new[]
            {
        "company", "co", "corporation", "corp", "limited", "ltd",
        "incorporated", "inc", "plc", "llc", "pty", "gmbh", "sa",
        "ag", "group", "holding", "holdings", "services", "solutions"
    };

            var words = normalized.Split(' ')
                .Where(word => !suffixes.Contains(word) && word.Length > 1)
                .ToArray();

            return string.Join(" ", words);
        }

        private double CalculatePartialMatchScore(string name1, string name2)
        {
            var words1 = name1.Split(' ');
            var words2 = name2.Split(' ');

            int matches = 0;
            foreach (var word1 in words1)
            {
                foreach (var word2 in words2)
                {
                    if (word1.Length > 3 && word2.Length > 3)
                    {
                        if (CalculateJaroWinklerSimilarity(word1, word2) > 0.85)
                            matches++;
                    }
                }
            }

            return (double)matches / Math.Max(words1.Length, words2.Length);
        }

        private List<string> GetCommonWords(string name1, string name2)
        {
            var words1 = name1.ToLowerInvariant().Split(' ').Where(w => w.Length > 3).ToList();
            var words2 = name2.ToLowerInvariant().Split(' ').Where(w => w.Length > 3).ToList();

            return words1.Intersect(words2).ToList();
        }

        public async Task<double> CalculateCompanyNameSimilarityAsync(string companyName1, string companyName2)
        {
            return CalculateCompanyNameSimilarity(companyName1, companyName2);
        }

        // Add this method to calculate company name similarity
        private double CalculateCompanyNameSimilarity(string name1, string name2)
        {
            if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
                return 0;

            // Normalize the names
            var normalizedName1 = NormalizeCompanyName(name1);
            var normalizedName2 = NormalizeCompanyName(name2);

            // Use multiple similarity algorithms and take the best score
            var scores = new List<double>
    {
        CalculateJaroWinklerSimilarity(normalizedName1, normalizedName2),
        CalculateNormalizedLevenshteinSimilarity(normalizedName1, normalizedName2),
        CalculateCosineSimilarity(normalizedName1, normalizedName2)
    };

            // Also check for acronyms and abbreviations
            var acronymScore = CalculateAcronymSimilarity(name1, name2);
            if (acronymScore > 0.8)
                scores.Add(acronymScore);

            return scores.Max();
        }



        private string NormalizeCompanyName(string companyName)
        {
            if (string.IsNullOrEmpty(companyName))
                return companyName;

            // Convert to lowercase
            var normalized = companyName.ToLowerInvariant();

            // Remove common words and abbreviations
            var commonWords = new HashSet<string>
    {
        "the", "and", "of", "for", "in", "on", "at", "to", "a", "an",
        "company", "co", "corporation", "corp", "limited", "ltd",
        "incorporated", "inc", "plc", "llc", "pty", "gmbh"
    };

            // Split into words and remove common words
            var words = normalized.Split(new[] { ' ', '-', ',', '&' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(word => !commonWords.Contains(word))
                .ToArray();

            // Rejoin the meaningful words
            return string.Join(" ", words);
        }
        // Add this method to check for acronym matches
        private double CalculateAcronymSimilarity(string name1, string name2)
        {
            // Extract acronyms from company names
            var acronym1 = ExtractAcronym(name1);
            var acronym2 = ExtractAcronym(name2);

            if (string.IsNullOrEmpty(acronym1) || string.IsNullOrEmpty(acronym2))
                return 0;

            // Check if one is the acronym of the other
            if (acronym1.Equals(acronym2, StringComparison.OrdinalIgnoreCase))
                return 0.9;

            // Check if acronyms are similar
            return CalculateJaroWinklerSimilarity(acronym1.ToLowerInvariant(), acronym2.ToLowerInvariant());
        }

        // Add this method to extract acronyms
        private string ExtractAcronym(string companyName)
        {
            if (string.IsNullOrEmpty(companyName))
                return string.Empty;

            // Split by common separators and take first letters
            var words = companyName.Split(new[] { ' ', '-', ',', '&' }, StringSplitOptions.RemoveEmptyEntries);

            // For long names, take first letters of each word
            if (words.Length > 1)
            {
                var acronym = new string(words.Select(w => w[0]).ToArray());
                return acronym.ToUpperInvariant();
            }

            // For single word names, take first 3-4 letters
            if (companyName.Length >= 4)
                return companyName.Substring(0, 4).ToUpperInvariant();

            return companyName.ToUpperInvariant();
        }
        public async Task<List<TenderParticipationDto>> GetTopParticipatingCompaniesAsync(int limit = 20)
        {
            try
            {
                _logger.LogInformation($"Getting top {limit} participating companies");

                var companyParticipation = new Dictionary<string, CompanyParticipationData>();

                // Analyze current tenders (more pages for better results)
                for (int page = 1; page <= 15; page++) // Increased to 15 pages
                {
                    try
                    {
                        var batch = await _scraperService.ScrapePageAsync(page);
                        foreach (var tender in batch.Tenders)
                        {
                            if (!string.IsNullOrEmpty(tender.ProcuringEntity))
                            {
                                var entity = tender.ProcuringEntity.Trim();
                                if (!companyParticipation.ContainsKey(entity))
                                {
                                    companyParticipation[entity] = new CompanyParticipationData
                                    {
                                        CompanyName = entity,
                                        TenderCount = 0,
                                        Tenders = new List<TenderInfoDto>()
                                    };
                                }

                                companyParticipation[entity].TenderCount++;

                                if (!string.IsNullOrEmpty(tender.TenderId))
                                {
                                    companyParticipation[entity].Tenders.Add(new TenderInfoDto
                                    {
                                        TenderId = tender.TenderId,
                                        Title = tender.Title,
                                        ReferenceNumber = tender.ReferenceNumber,
                                        PublishDate = tender.PublishDate
                                    });
                                }
                            }
                        }

                        // Add delay to avoid overwhelming the server
                        await Task.Delay(100);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error scraping page {page} for top companies");
                    }
                }

                // Convert to DTOs
                var topCompanies = companyParticipation
                    .Where(kvp => kvp.Value.TenderCount > 0)
                    .OrderByDescending(kvp => kvp.Value.TenderCount)
                    .Take(limit)
                    .Select(kvp => new TenderParticipationDto
                    {
                        CompanyName = kvp.Key,
                        TenderCount = kvp.Value.TenderCount,
                        ParticipatedTenders = kvp.Value.Tenders.Take(5).ToList(), // Show top 5 tenders
                        Role = "Procuring Entity",
                        Status = "Active"
                    })
                    .ToList();

                return topCompanies;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting top participating companies");
                throw;
            }
        }

        // Helper class for company participation data
        private class CompanyParticipationData
        {
            public string CompanyName { get; set; }
            public int TenderCount { get; set; }
            public List<TenderInfoDto> Tenders { get; set; }
        }


        public async Task<List<TenderWithSimilarityDto>> FindTendersByCategoryWithSimilarityAsync(
         string targetCategory,
         double similarityThreshold = 0.7)
        {
            try
            {
                _logger.LogInformation($"Finding tenders by category with similarity: {targetCategory} (threshold: {similarityThreshold})");

                var allTenders = await GetTendersForComparisonAsync();
                var similarTenders = new List<TenderWithSimilarityDto>();

                foreach (var tender in allTenders)
                {
                    if (tender.CategoryNames == null || !tender.CategoryNames.Any())
                        continue;

                    // Calculate maximum category similarity for this tender
                    double maxCategorySimilarity = 0;
                    string bestMatchingCategory = "";

                    foreach (var category in tender.CategoryNames)
                    {
                        var similarity = CalculateCategoryNameSimilarity(category, targetCategory);
                        if (similarity > maxCategorySimilarity)
                        {
                            maxCategorySimilarity = similarity;
                            bestMatchingCategory = category;
                        }
                    }

                    if (maxCategorySimilarity >= similarityThreshold)
                    {
                        // Calculate overall similarity for ranking
                        var overallSimilarity = maxCategorySimilarity;

                        // Create extended tender with similarity info
                        var tenderWithSimilarity = new TenderWithSimilarityDto
                        {
                            // Copy all tender properties
                            Id = tender.Id,
                            TenderId = tender.TenderId,
                            ReferenceNumber = tender.ReferenceNumber,
                            Title = tender.Title,
                            CategoryCodes = tender.CategoryCodes,
                            CategoryNames = tender.CategoryNames,
                            ProcuringEntity = tender.ProcuringEntity,
                            Scope = tender.Scope,
                            PublishDate = tender.PublishDate,
                            ClosingDate = tender.ClosingDate,
                            DetailsUrl = tender.DetailsUrl,
                            ScrapedAt = tender.ScrapedAt,
                            SourceUrl = tender.SourceUrl,
                            PageNumber = tender.PageNumber,

                            // Add similarity properties
                            SimilarityScore = overallSimilarity,
                            MatchReasons = new List<string>
                    {
                        $"Category match: '{bestMatchingCategory}' with {Math.Round(maxCategorySimilarity * 100)}% similarity to '{targetCategory}'"
                    },
                            CategorySimilarity = maxCategorySimilarity,
                            EntitySimilarity = 0,
                            TextSimilarity = maxCategorySimilarity,
                            TargetCategory = targetCategory
                        };

                        similarTenders.Add(tenderWithSimilarity);
                    }
                }

                return similarTenders
                    .OrderByDescending(s => s.SimilarityScore)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error finding tenders by category: {targetCategory}");
                throw;
            }
        }
        // Add this new method for category name similarity
        // Add this public method to TenderSimilarityService
        public double CalculateCategoryNameSimilarity(string category1, string category2)
        {
            if (string.IsNullOrEmpty(category1) || string.IsNullOrEmpty(category2))
                return 0;

            // Use multiple similarity algorithms for better accuracy
            var similarities = new List<double>
    {
        CalculateJaroWinklerSimilarity(category1.ToLowerInvariant(), category2.ToLowerInvariant()),
        CalculateNormalizedLevenshteinSimilarity(category1.ToLowerInvariant(), category2.ToLowerInvariant()),
        CalculateCosineSimilarity(category1.ToLowerInvariant(), category2.ToLowerInvariant())
    };

            // Also check for partial matches and abbreviations
            if (IsCategoryAbbreviation(category1, category2) || IsCategoryAbbreviation(category2, category1))
                similarities.Add(0.9);

            // Check for word overlap
            var wordSimilarity = CalculateCategoryWordSimilarity(category1, category2);
            if (wordSimilarity > 0.5)
                similarities.Add(wordSimilarity);

            return similarities.Max();
        }

        // Make this method public
        public bool IsCategoryAbbreviation(string category1, string category2)
        {
            if (string.IsNullOrEmpty(category1) || string.IsNullOrEmpty(category2))
                return false;

            var shortCat = category1.Length < category2.Length ? category1 : category2;
            var longCat = category1.Length < category2.Length ? category2 : category1;

            // Check if all characters in short category appear in order in long category
            int j = 0;
            for (int i = 0; i < longCat.Length && j < shortCat.Length; i++)
            {
                if (char.ToLowerInvariant(longCat[i]) == char.ToLowerInvariant(shortCat[j]))
                    j++;
            }

            return j == shortCat.Length;
        }

        // Helper method for category word similarity
        private double CalculateCategoryWordSimilarity(string category1, string category2)
        {
            var words1 = category1.ToLowerInvariant().Split(' ', '-', ',', '/')
                .Where(w => w.Length > 2)
                .ToHashSet();

            var words2 = category2.ToLowerInvariant().Split(' ', '-', ',', '/')
                .Where(w => w.Length > 2)
                .ToHashSet();

            if (!words1.Any() || !words2.Any())
                return 0;

            var commonWords = words1.Intersect(words2).Count();
            var totalWords = words1.Count + words2.Count;

            return (2.0 * commonWords) / totalWords;
        }

     


    }
}