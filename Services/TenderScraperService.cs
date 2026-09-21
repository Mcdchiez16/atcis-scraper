using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Models;

namespace ZimbabweTenderAPI.Services
{
    public interface ITenderScraperService
    {
        // Existing methods
        Task<TenderBatch> ScrapePageAsync(int pageNumber);
        Task<List<Tender>> ScrapeMultiplePagesAsync(int startPage, int endPage);
        Task<int> GetTotalPagesAsync();
        Task<TenderBatch> SearchTendersAsync(string keyword, int? page = 1);
        Task<List<Tender>> GetTendersByEntityAsync(string entityName);
        Task<List<Tender>> GetClosingSoonTendersAsync(int days = 7);

        // New methods for award notices
        Task<PaginatedResponse<AwardNoticeDto>> ScrapeAwardNoticesAsync(int page = 1);
        Task<PaginatedResponse<AwardNoticeDto>> SearchAwardNoticesAsync(AwardNoticeSearchRequest request);

        // New methods for annual procurement plans
        Task<PaginatedResponse<AnnualProcurementPlanDto>> ScrapeAnnualProcurementPlansAsync(int page = 1);
        Task<PaginatedResponse<AnnualProcurementPlanDto>> SearchAnnualProcurementPlansAsync(AnnualProcurementPlanSearchRequest request);

        // New method for past tenders
        Task<PaginatedResponse<Tender>> ScrapePastTendersAsync(int page = 1);
        Task<PaginatedResponse<Tender>> SearchPastTendersAsync(ScrapeRequest request);

        // New methods for similarity analysis
        Task<List<TenderSimilarityDto>> AnalyzeTenderSimilarityAsync(string tenderId);
        Task<List<TenderSimilarityDto>> FindSimilarTendersAsync(string title, int maxResults = 10);

        // New methods for company participation tracking
        Task<List<TenderParticipationDto>> GetCompanyParticipationAsync(string companyName);
        Task<List<TenderParticipationDto>> GetTopParticipatingCompaniesAsync(int limit = 20);

        Task<List<AwardNoticeDto>> ScrapeMultipleAwardNoticePagesAsync(int startPage, int endPage);
        Task<List<AnnualProcurementPlanDto>> ScrapeMultipleAnnualProcurementPlanPagesAsync(int startPage, int endPage);
        Task<List<Tender>> ScrapeMultiplePastTenderPagesAsync(int startPage, int endPage);
        Task<PaginatedResponse<AwardNoticeDto>> SearchAwardNoticesByStringAsync(string searchTerm, int page = 1, int pageSize = 20);
        Task<TenderDetail> GetTenderDetailAsync(string tenderId);
        Task<List<TenderDetail>> GetTenderDetailsBatchAsync(List<string> tenderIds);
        Task<List<TenderWithSimilarityDto>> FindTendersByCategoryWithSimilarityAsync(
      string targetCategory,
      double similarityThreshold = 0.7);
        Task<AnnualProcurementPlanDetailDto> GetAnnualProcurementPlanDetailAsync(string url);
        Task<ZimbabweTenderDetailDto> GetZimbabweTenderDetailsAsync(string tenderId);
        Task<List<ZimbabweTenderDocumentDto>> GetZimbabweTenderDocumentsAsync(string tenderId, string? sessionCookie = null);
        void SetPrazSessionCookie(string cookie);
    }

    public class TenderScraperService : ITenderScraperService
    {
            private readonly HttpClient _httpClient;
            private readonly IMemoryCache _cache;
            private readonly ILogger<TenderScraperService> _logger;
            private const string BaseUrl = "https://egp.praz.org.zw";
            private const string TenderListPath = "/index?url=egp-SW5kZXhlcy9pbmRleA%3D%3D";
            private const string CacheKeyTotalPages = "TotalPages";
            private const int CacheDurationMinutes = 30;

            public TenderScraperService(HttpClient httpClient, IMemoryCache cache, ILogger<TenderScraperService> logger)
            {
                _httpClient = httpClient;
                _httpClient.Timeout = TimeSpan.FromSeconds(30);
                _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                _cache = cache;
                _logger = logger;

                // Get ILoggerFactory from the service provider
                var serviceProvider = new ServiceCollection()
                    .AddLogging(builder => builder.AddConsole().AddDebug())
                    .BuildServiceProvider();

                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                var similarityLogger = loggerFactory.CreateLogger<TenderSimilarityService>();

                _similarityService = new TenderSimilarityService(this, cache, similarityLogger, httpClient);
            }

            public async Task<List<TenderWithSimilarityDto>> FindTendersByCategoryWithSimilarityAsync(
         string targetCategory,
         double similarityThreshold = 0.7)
            {
                return await _similarityService.FindTendersByCategoryWithSimilarityAsync(targetCategory, similarityThreshold);
            }
            // In TenderScraperService
            public async Task<TenderDetail> GetTenderDetailAsync(string tenderId)
            {
                try
                {
                    // Find the tender to get its details URL
                    var tender = await FindTenderByIdAsync(tenderId);
                    if (tender == null || string.IsNullOrEmpty(tender.DetailsUrl))
                        return null;

                    var html = await _httpClient.GetStringAsync(tender.DetailsUrl);
                    return ParseTenderDetailHtml(html, tenderId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error fetching tender details for {tenderId}");
                    return null;
                }
            }

            private async Task<Tender> FindTenderByIdAsync(string tenderId)
            {
                // Search through a few pages to find the tender
                for (int page = 1; page <= 5; page++)
                {
                    var batch = await ScrapePageAsync(page);
                    var tender = batch.Tenders.FirstOrDefault(t => t.TenderId == tenderId);
                    if (tender != null)
                        return tender;
                }
                return null;
            }

            private TenderDetail ParseTenderDetailHtml(string html, string tenderId)
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var detail = new TenderDetail
                {
                    TenderId = tenderId,
                    ScrapedAt = DateTime.UtcNow
                };

                // Extract key information from the tender detail page
                // This will vary based on the website structure

                // Try to get description/scope
                var scopeElement = doc.DocumentNode.SelectSingleNode("//td[contains(text(), 'Description of Procurement')]/following-sibling::td") ??
                                   doc.DocumentNode.SelectSingleNode("//td[contains(text(), 'Scope')]/following-sibling::td") ??
                                   doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'description')]") ??
                                   doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'scope')]");

                detail.Description = scopeElement?.InnerText?.Trim();

                // Try to get requirements/specifications
                var requirementsElement = doc.DocumentNode.SelectSingleNode("//td[contains(text(), 'Requirements')]/following-sibling::td") ??
                                          doc.DocumentNode.SelectSingleNode("//td[contains(text(), 'Specifications')]/following-sibling::td") ??
                                          doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'requirements')]");

                detail.Requirements = requirementsElement?.InnerText?.Trim();

                // Try to get evaluation criteria
                var evaluationElement = doc.DocumentNode.SelectSingleNode("//td[contains(text(), 'Evaluation')]/following-sibling::td") ??
                                        doc.DocumentNode.SelectSingleNode("//td[contains(text(), 'Criteria')]/following-sibling::td");

                detail.EvaluationCriteria = evaluationElement?.InnerText?.Trim();

                // Try to get budget/value
                var budgetElement = doc.DocumentNode.SelectSingleNode("//td[contains(text(), 'Budget')]/following-sibling::td") ??
                                    doc.DocumentNode.SelectSingleNode("//td[contains(text(), 'Value')]/following-sibling::td");

                detail.BudgetEstimate = budgetElement?.InnerText?.Trim();

                // Get full HTML content for AI analysis
                detail.FullContent = doc.DocumentNode.InnerText?.Trim();

                return detail;
            }

            public async Task<List<TenderDetail>> GetTenderDetailsBatchAsync(List<string> tenderIds)
            {
                var details = new List<TenderDetail>();

                // Process in batches to avoid overwhelming the server
                var batchSize = 3;
                for (int i = 0; i < tenderIds.Count; i += batchSize)
                {
                    var batch = tenderIds.Skip(i).Take(batchSize).ToList();
                    var tasks = batch.Select(id => GetTenderDetailAsync(id));
                    var results = await Task.WhenAll(tasks);

                    details.AddRange(results.Where(d => d != null));

                    // Respectful delay between batches
                    if (i + batchSize < tenderIds.Count)
                        await Task.Delay(1000);
                }

                return details;
            }

            public async Task<TenderBatch> ScrapePageAsync(int pageNumber)
            {
                try
                {
                    _logger.LogInformation("Scraping page {Page}", pageNumber);

                    var url = $"{BaseUrl}{TenderListPath}&page={pageNumber}&direction=BulletinBoardLive.id";
                    var html = await _httpClient.GetStringAsync(url);

                    var batch = ParseTenderListHtml(html, pageNumber);
                    batch.HasMorePages = pageNumber < await GetTotalPagesAsync();
                    batch.NextPage = batch.HasMorePages ? pageNumber + 1 : null;

                    return batch;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scraping page {Page}", pageNumber);
                    throw;
                }
            }


            public async Task<PaginatedResponse<AwardNoticeDto>> SearchAwardNoticesByStringAsync(string searchTerm, int page = 1, int pageSize = 20)
            {
                try
                {
                    _logger.LogInformation($"Simple award notice search for: '{searchTerm}', page: {page}");

                    // Get multiple pages to search through
                    var allAwardNotices = new List<AwardNoticeDto>();
                    var pagesToSearch = Math.Min(5, await GetTotalAwardNoticePagesAsync());

                    for (int p = 1; p <= pagesToSearch; p++)
                    {
                        var pageNotices = await ScrapeAwardNoticesAsync(p);
                        var matchingNotices = FilterAwardNoticesByString(pageNotices.Items, searchTerm);
                        allAwardNotices.AddRange(matchingNotices);

                        // Break early if we have enough results
                        if (allAwardNotices.Count >= 100) break;

                        await Task.Delay(300); // Respectful delay
                    }

                    // Apply pagination
                    var skipCount = (page - 1) * pageSize;
                    var paginatedResults = allAwardNotices
                        .Skip(skipCount)
                        .Take(pageSize)
                        .ToList();

                    return new PaginatedResponse<AwardNoticeDto>
                    {
                        Items = paginatedResults,
                        PageNumber = page,
                        PageSize = pageSize,
                        TotalCount = allAwardNotices.Count,
                        TotalPages = (int)Math.Ceiling(allAwardNotices.Count / (double)pageSize),
                        HasPreviousPage = page > 1,
                        HasNextPage = skipCount + pageSize < allAwardNotices.Count
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in simple award notice search for '{searchTerm}'");
                    throw;
                }
            }

            private List<AwardNoticeDto> FilterAwardNoticesByString(List<AwardNoticeDto> awardNotices, string searchTerm)
            {
                if (string.IsNullOrWhiteSpace(searchTerm))
                    return awardNotices;

                var searchTerms = SplitSearchTermsAdvanced(searchTerm);

                // For simple search, we might want OR logic (matching any term)
                // but for better relevance, let's use AND logic
                return awardNotices.Where(notice =>
                {
                    // Combine all searchable text
                    var searchText = $"{notice.AwardNoticeNumber} {notice.TenderId} {notice.AwardTitle} {notice.Awardee}".ToLowerInvariant();
                    var searchTermsLower = searchTerms.Select(t => t.ToLowerInvariant()).ToList();

                    // Check if ALL search terms are found in the combined text
                    // This creates an "AND" search by default
                    return searchTermsLower.All(term =>
                        searchText.Contains(term));
                }).ToList();
            }
            private async Task<int> GetTotalAwardNoticePagesAsync()
            {
                var cacheKey = "TotalAwardNoticePages";
                if (_cache.TryGetValue(cacheKey, out int cachedPages))
                    return cachedPages;

                try
                {
                    var firstPage = await ScrapeAwardNoticesAsync(1);
                    var totalPages = (int)Math.Ceiling(firstPage.TotalCount / 20.0);

                    _cache.Set(cacheKey, totalPages, TimeSpan.FromMinutes(CacheDurationMinutes));
                    return totalPages;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting total award notice pages");
                    return 5; // Default fallback
                }
            }

            public async Task<List<AwardNoticeDto>> ScrapeMultipleAwardNoticePagesAsync(int startPage, int endPage)
            {
                var allAwardNotices = new ConcurrentBag<AwardNoticeDto>();
                var pages = Enumerable.Range(startPage, endPage - startPage + 1);

                await Parallel.ForEachAsync(pages, new ParallelOptions { MaxDegreeOfParallelism = 3 },
                    async (page, ct) =>
                    {
                        try
                        {
                            var response = await ScrapeAwardNoticesAsync(page);
                            foreach (var notice in response.Items)
                                allAwardNotices.Add(notice);

                            await Task.Delay(300, ct); // Respectful delay
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error scraping award notice page {Page}", page);
                        }
                    });

                return allAwardNotices.OrderByDescending(a => a.AwardDate).ToList();
            }

            public async Task<List<AnnualProcurementPlanDto>> ScrapeMultipleAnnualProcurementPlanPagesAsync(int startPage, int endPage)
            {
                var allPlans = new ConcurrentBag<AnnualProcurementPlanDto>();
                var pages = Enumerable.Range(startPage, endPage - startPage + 1);

                await Parallel.ForEachAsync(pages, new ParallelOptions { MaxDegreeOfParallelism = 3 },
                    async (page, ct) =>
                    {
                        try
                        {
                            var response = await ScrapeAnnualProcurementPlansAsync(page);
                            foreach (var plan in response.Items)
                                allPlans.Add(plan);

                            await Task.Delay(300, ct); // Respectful delay
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error scraping annual procurement plan page {Page}", page);
                        }
                    });

                return allPlans.OrderBy(p => p.ProcuringEntity).ThenBy(p => p.Year).ToList();
            }

            public async Task<List<Tender>> ScrapeMultiplePastTenderPagesAsync(int startPage, int endPage)
            {
                var allPastTenders = new ConcurrentBag<Tender>();
                var pages = Enumerable.Range(startPage, endPage - startPage + 1);

                await Parallel.ForEachAsync(pages, new ParallelOptions { MaxDegreeOfParallelism = 3 },
                    async (page, ct) =>
                    {
                        try
                        {
                            var response = await ScrapePastTendersAsync(page);
                            foreach (var tender in response.Items)
                                allPastTenders.Add(tender);

                            await Task.Delay(300, ct); // Respectful delay
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error scraping past tender page {Page}", page);
                        }
                    });

                return allPastTenders.OrderByDescending(t => t.ClosingDate).ToList();
            }


            public async Task<List<Tender>> ScrapeMultiplePagesAsync(int startPage, int endPage)
            {
                var allTenders = new ConcurrentBag<Tender>();
                var pages = Enumerable.Range(startPage, endPage - startPage + 1);

                await Parallel.ForEachAsync(pages, new ParallelOptions { MaxDegreeOfParallelism = 3 },
                    async (page, ct) =>
                    {
                        try
                        {
                            var batch = await ScrapePageAsync(page);
                            foreach (var tender in batch.Tenders)
                                allTenders.Add(tender);
                            await Task.Delay(500, ct);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error scraping page {Page}", page);
                        }
                    });

                return allTenders.OrderByDescending(t => t.PublishDate).ToList();
            }

            public async Task<int> GetTotalPagesAsync()
            {
                if (_cache.TryGetValue(CacheKeyTotalPages, out int cachedPages))
                    return cachedPages;

                try
                {
                    var url = $"{BaseUrl}{TenderListPath}&page=1";
                    var html = await _httpClient.GetStringAsync(url);
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    // Parse "Page 1 of 26, showing 20 record(s) out of 510 total"
                    var paginatorText = doc.DocumentNode.SelectSingleNode("//div[@class='paginator']")?.InnerText ?? "";
                    var match = Regex.Match(paginatorText, @"Page \d+ of (\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int totalPages))
                    {
                        _cache.Set(CacheKeyTotalPages, totalPages, TimeSpan.FromMinutes(CacheDurationMinutes));
                        return totalPages;
                    }

                    // Fallback: check last page link
                    var lastLink = doc.DocumentNode.SelectSingleNode("//li[@class='last']/a");
                    if (lastLink != null)
                    {
                        var href = lastLink.GetAttributeValue("href", "");
                        var pageMatch = Regex.Match(href, @"page=(\d+)");
                        if (pageMatch.Success && int.TryParse(pageMatch.Groups[1].Value, out totalPages))
                        {
                            _cache.Set(CacheKeyTotalPages, totalPages, TimeSpan.FromMinutes(CacheDurationMinutes));
                            return totalPages;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting total pages");
                }

                return 26; // Default fallback
            }






            public async Task<TenderBatch> SearchTendersAsync(string keyword, int? page = 1)
            {
                try
                {
                    _logger.LogInformation($"Advanced search for: '{keyword}', page: {page}");

                    var allTenders = new List<Tender>();
                    int currentPage = page ?? 1;
                    int maxPages = Math.Min(currentPage + 5, await GetTotalPagesAsync());

                    // Advanced search: Split into individual terms with AND/OR logic
                    var searchTerms = SplitSearchTermsAdvanced(keyword);

                    _logger.LogDebug($"Search terms parsed: {string.Join(", ", searchTerms)}");

                    for (int p = currentPage; p <= maxPages; p++)
                    {
                        var batch = await ScrapePageAsync(p);

                        // Apply advanced filtering
                        var filtered = batch.Tenders.Where(t =>
                        {
                            var title = t.Title ?? "";
                            var entity = t.ProcuringEntity ?? "";
                            var scope = t.Scope ?? "";
                            var categories = string.Join(" ", t.CategoryNames ?? new List<string>());

                            var fullText = $"{title} {entity} {scope} {categories}".ToLowerInvariant();

                            // Apply AND logic for all terms
                            return searchTerms.All(term =>
                                fullText.Contains(term.ToLowerInvariant()));
                        }).ToList();

                        allTenders.AddRange(filtered);

                        // Break if we have enough results or reach max pages
                        if (allTenders.Count >= 50 || p >= maxPages)
                            break;

                        await Task.Delay(300); // Respectful delay
                    }

                    // Apply ranking based on relevance
                    var rankedResults = allTenders.Select(t => new
                    {
                        Tender = t,
                        Score = CalculateSearchScore(t, searchTerms)
                    })
                    .OrderByDescending(x => x.Score)
                    .Select(x => x.Tender)
                    .ToList();

                    return new TenderBatch
                    {
                        Tenders = rankedResults,
                        Page = currentPage,
                        TotalTenders = rankedResults.Count,
                        ScrapedAt = DateTime.UtcNow,
                        HasMorePages = maxPages < await GetTotalPagesAsync(),
                        NextPage = maxPages < await GetTotalPagesAsync() ? currentPage + 1 : null
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in advanced search for '{keyword}'");
                    throw;
                }
            }

            // Advanced search term parsing with AND/OR support
            private List<string> SplitSearchTermsAdvanced(string searchQuery)
            {
                if (string.IsNullOrWhiteSpace(searchQuery))
                    return new List<string>();

                // Remove quotes and parentheses for now (could implement more complex logic)
                var cleaned = searchQuery.Replace("\"", "").Replace("(", "").Replace(")", "");

                // Split by common operators
                var parts = cleaned.Split(new[] { " AND ", " and ", " OR ", " or " },
                    StringSplitOptions.RemoveEmptyEntries);

                var terms = new List<string>();

                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        // Split into individual words for phrase searches
                        if (trimmed.Contains(' '))
                        {
                            // Add the full phrase
                            terms.Add(trimmed);

                            // Also add significant individual words
                            var words = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                .Where(w => w.Length > 3 && !IsCommonWord(w))
                                .ToList();

                            terms.AddRange(words);
                        }
                        else
                        {
                            terms.Add(trimmed);
                        }
                    }
                }

                // Remove duplicates and common stop words
                return terms.Distinct()
                            .Where(t => !IsCommonWord(t))
                            .ToList();
            }

            // Calculate search relevance score
            private double CalculateSearchScore(Tender tender, List<string> searchTerms)
            {
                double score = 0;

                var title = tender.Title ?? "";
                var entity = tender.ProcuringEntity ?? "";
                var scope = tender.Scope ?? "";
                var categories = string.Join(" ", tender.CategoryNames ?? new List<string>());

                // Check each search term
                foreach (var term in searchTerms)
                {
                    var termLower = term.ToLowerInvariant();

                    // Title matches get highest weight
                    if (title.ToLowerInvariant().Contains(termLower))
                        score += 3.0;

                    // Entity matches get medium weight
                    if (entity.ToLowerInvariant().Contains(termLower))
                        score += 2.0;

                    // Category matches
                    if (categories.ToLowerInvariant().Contains(termLower))
                        score += 1.5;

                    // Scope/content matches get lower weight
                    if (scope.ToLowerInvariant().Contains(termLower))
                        score += 1.0;

                    // Partial word matches
                    if (ContainsPartialMatch(title, term) || ContainsPartialMatch(entity, term))
                        score += 0.5;
                }

                // Bonus for exact phrase matches
                var fullSearchPhrase = string.Join(" ", searchTerms).ToLowerInvariant();
                if (title.ToLowerInvariant().Contains(fullSearchPhrase))
                    score += 5.0;

                return score;
            }

            private bool ContainsPartialMatch(string source, string term)
            {
                if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(term) || term.Length < 3)
                    return false;

                return source.ToLowerInvariant().Contains(term.ToLowerInvariant().Substring(0, Math.Min(3, term.Length)));
            }
















            // Helper method to split search terms intelligently
            private List<string> SplitSearchTerms(string searchQuery)
            {
                if (string.IsNullOrWhiteSpace(searchQuery))
                    return new List<string>();

                // Remove extra spaces and split by common conjunctions and separators
                var terms = new List<string>();

                // Split by common conjunctions: "and", "or", "with", "for"
                var pattern = @"\s+(and|or|with|for)\s+";
                var parts = Regex.Split(searchQuery, pattern, RegexOptions.IgnoreCase);

                foreach (var part in parts)
                {
                    // Remove any trailing parentheses issues
                    var cleanPart = part.Trim();

                    // Split into individual words for longer phrases
                    if (cleanPart.Contains(' '))
                    {
                        // For longer phrases, also include individual significant words
                        var words = cleanPart.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                            .Where(w => w.Length > 3) // Only include meaningful words
                            .Where(w => !IsCommonWord(w)) // Filter out common words
                            .ToList();

                        // Add the full phrase
                        terms.Add(cleanPart);

                        // Add individual significant words
                        terms.AddRange(words);
                    }
                    else
                    {
                        terms.Add(cleanPart);
                    }
                }

                // Remove duplicates and return
                return terms.Distinct().ToList();
            }



            // Helper method to identify common words that shouldn't be searched individually
            private bool IsCommonWord(string word)
            {
                var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "a", "about", "above", "after", "again", "against", "all", "am",
                "an", "and", "any", "are", "aren't", "as", "at",

                "be", "because", "been", "before", "being", "below", "between",
                "both", "but", "by",

                "can", "can't", "cannot", "could", "couldn't",

                "did", "didn't", "do", "does", "doesn't", "doing", "don't", "down",
                "during",

                "each",

                "few", "for", "from", "further",

                "had", "hadn't", "has", "hasn't", "have", "haven't", "having",
                "he", "he'd", "he'll", "he's", "her", "here", "here's", "hers",
                "herself", "him", "himself", "his", "how", "how's",

                "i", "i'd", "i'll", "i'm", "i've", "if", "in", "into", "is",
                "isn't", "it", "it's", "its", "itself",

                "let's",

                "me", "more", "most", "mustn't", "my", "myself",

                "no", "nor", "not",

                "of", "off", "on", "once", "only", "or", "other", "ought", "our",
                "ours", "ourselves", "out", "over", "own",

                "same", "she", "she'd", "she'll", "she's", "should", "shouldn't",
                "so", "some", "such",

                "than", "that", "that's", "the", "their", "theirs", "them",
                "themselves", "then", "there", "there's", "these", "they",
                "they'd", "they'll", "they're", "they've", "this", "those",
                "through", "to", "too",

                "under", "until", "up",

                "very",

                "was", "wasn't", "we", "we'd", "we'll", "we're", "we've", "were",
                "weren't", "what", "what's", "when", "when's", "where", "where's",
                "which", "while", "who", "who's", "whom", "why", "why's", "will",
                "with", "won't", "would", "wouldn't",

                "you", "you'd", "you'll", "you're", "you've", "your", "yours",
                "yourself", "yourselves"
            };


                return commonWords.Contains(word.ToLower());
            }
            public async Task<List<Tender>> GetTendersByEntityAsync(string entityName)
            {
                var results = new List<Tender>();
                int totalPages = Math.Min(10, await GetTotalPagesAsync());

                for (int page = 1; page <= totalPages; page++)
                {
                    var batch = await ScrapePageAsync(page);
                    results.AddRange(batch.Tenders.Where(t => Contains(t.ProcuringEntity, entityName)));
                    await Task.Delay(300);
                }

                return results;
            }

            public async Task<List<Tender>> GetClosingSoonTendersAsync(int days = 7)
            {
                var cutoff = DateTime.UtcNow.AddDays(days);
                var results = new List<Tender>();

                for (int page = 1; page <= 5; page++)
                {
                    var batch = await ScrapePageAsync(page);
                    results.AddRange(batch.Tenders.Where(t =>
                        t.ClosingDate.HasValue &&
                        t.ClosingDate > DateTime.UtcNow &&
                        t.ClosingDate <= cutoff));
                }

                return results.OrderBy(t => t.ClosingDate).ToList();
            }

            private TenderBatch ParseTenderListHtml(string html, int pageNumber)
            {
                var batch = new TenderBatch
                {
                    Page = pageNumber,
                    ScrapedAt = DateTime.UtcNow
                };

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // Select table rows (skip header)
                var rows = doc.DocumentNode.SelectNodes("//table[contains(@class,'table')]//tr[td]");
                if (rows == null)
                {
                    _logger.LogWarning("No tender rows found on page {Page}", pageNumber);
                    return batch;
                }

                foreach (var row in rows)
                {
                    var cells = row.SelectNodes("td");
                    if (cells == null || cells.Count < 9) continue;

                    try
                    {
                        var tender = new Tender
                        {
                            Id = Guid.NewGuid().ToString(),
                            PageNumber = pageNumber,
                            SourceUrl = $"{BaseUrl}{TenderListPath}&page={pageNumber}"
                        };

                        // Column 0: Tender ID with link
                        var idLink = cells[0].SelectSingleNode(".//a");
                        if (idLink != null)
                        {
                            tender.TenderId = idLink.InnerText.Trim();
                            var href = idLink.GetAttributeValue("href", "");
                            tender.DetailsUrl = href.StartsWith("http") ? href : BaseUrl + href;
                        }
                        else
                        {
                            tender.TenderId = GetCellText(cells[0]);
                        }

                        // Column 1: Reference Number
                        tender.ReferenceNumber = GetCellText(cells[1]);

                        // Column 2: Title
                        tender.Title = GetCellText(cells[2]);

                        // Column 3: Category Codes (comma-separated)
                        tender.CategoryCodes = SplitAndTrim(GetCellText(cells[3]));

                        // Column 4: Category Names (comma-separated)
                        tender.CategoryNames = SplitAndTrim(GetCellText(cells[4]));

                        // Column 5: Procuring Entity
                        tender.ProcuringEntity = GetCellText(cells[5]);

                        // Column 6: Scope
                        tender.Scope = GetCellText(cells[6]);

                        // Column 7: Publish Date (e.g., "03-Dec-2025 04:00 PM")
                        tender.PublishDate = ParseDate(GetCellText(cells[7]));

                        // Column 8: Closing Date
                        tender.ClosingDate = ParseDate(GetCellText(cells[8]));

                        batch.Tenders.Add(tender);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error parsing tender row");
                    }
                }

                batch.TotalTenders = batch.Tenders.Count;
                return batch;
            }

            private static string GetCellText(HtmlNode cell)
            {
                return HtmlEntity.DeEntitize(cell.InnerText).Trim();
            }

            private static List<string> SplitAndTrim(string text)
            {
                if (string.IsNullOrWhiteSpace(text)) return new List<string>();
                return text.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(s => s.Trim())
                           .Where(s => !string.IsNullOrEmpty(s))
                           .ToList();
            }

            private static DateTime? ParseDate(string dateText)
            {
                if (string.IsNullOrWhiteSpace(dateText)) return null;

                string[] formats =
                {
                "dd-MMM-yyyy hh:mm tt",
                "dd-MMM-yyyy h:mm tt",
                "dd-MMM-yyyy HH:mm",
                "dd-MMM-yyyy",
                "dd/MM/yyyy HH:mm",
                "dd/MM/yyyy"
            };

                foreach (var fmt in formats)
                {
                    if (DateTime.TryParseExact(dateText, fmt, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var dt))
                        return dt;
                }

                return DateTime.TryParse(dateText, out var fallback) ? fallback : null;
            }

            private static bool Contains(string source, string value)
            {
                if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
                    return false;

                // Check for exact match (case-insensitive)
                if (source.Contains(value, StringComparison.OrdinalIgnoreCase))
                    return true;

                // Handle plural/singular variations
                var normalizedSource = source.ToLower();
                var normalizedValue = value.ToLower();

                // Check if the source contains a variation of the search term
                if (IsPluralVariationMatch(normalizedSource, normalizedValue))
                    return true;

                // Check for word stemming (basic version)
                if (IsStemmedMatch(normalizedSource, normalizedValue))
                    return true;

                // NEW: Check for fuzzy matches (typos)
                if (IsFuzzyMatch(normalizedSource, normalizedValue))
                    return true;

                return false;
            }

            // NEW: Add fuzzy matching using Levenshtein distance
            private static bool IsFuzzyMatch(string source, string searchTerm, int maxDistance = 2)
            {
                // If search term is too short, don't use fuzzy matching
                if (searchTerm.Length < 4) return false;

                // Split source into words
                var sourceWords = source.Split(new[] { ' ', ',', ';', '.', '!', '?', ':', '-', '(', ')', '[', ']', '{', '}' },
                                              StringSplitOptions.RemoveEmptyEntries);

                foreach (var sourceWord in sourceWords)
                {
                    // Only check words of similar length
                    if (Math.Abs(sourceWord.Length - searchTerm.Length) <= maxDistance)
                    {
                        var distance = CalculateLevenshteinDistance(sourceWord, searchTerm);

                        // If distance is small relative to word length, it's a match
                        var maxAllowedDistance = Math.Max(1, searchTerm.Length / 4); // Allow 25% errors
                        if (distance <= maxAllowedDistance && distance <= maxDistance)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            // Levenshtein distance algorithm
            private static int CalculateLevenshteinDistance(string a, string b)
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

            // Helper method to handle plural/singular variations
            private static bool IsPluralVariationMatch(string source, string searchTerm)
            {
                // Common plural patterns
                if (searchTerm.EndsWith("s"))
                {
                    // If searching for plural, check for singular in source
                    var singularTerm = searchTerm.EndsWith("ies")
                        ? searchTerm.Substring(0, searchTerm.Length - 3) + "y"
                        : searchTerm.EndsWith("es")
                            ? searchTerm.Substring(0, searchTerm.Length - 2)
                            : searchTerm.Substring(0, searchTerm.Length - 1);

                    if (source.Contains(singularTerm) && singularTerm.Length > 2)
                        return true;
                }
                else
                {
                    // If searching for singular, check for plural in source
                    var pluralTerm = searchTerm.EndsWith("y")
                        ? searchTerm.Substring(0, searchTerm.Length - 1) + "ies"
                        : searchTerm + "s";

                    if (source.Contains(pluralTerm))
                        return true;
                }

                return false;
            }

            // Basic word stemming (simplified version)
            private static bool IsStemmedMatch(string source, string searchTerm)
            {
                // Common word endings to strip for stemming
                var wordEndings = new[] { "ing", "ed", "s", "es", "ies", "ly", "ment", "tion", "sion" };

                // Get the stem of the search term
                var searchStem = searchTerm;
                foreach (var ending in wordEndings)
                {
                    if (searchTerm.EndsWith(ending) && searchTerm.Length > ending.Length + 2)
                    {
                        searchStem = searchTerm.Substring(0, searchTerm.Length - ending.Length);
                        break;
                    }
                }

                // Check if source contains the stem
                if (source.Contains(searchStem) && searchStem.Length > 2)
                    return true;

                // Also check if any word in source shares the same stem
                var sourceWords = source.Split(new[] { ' ', ',', ';', '.', '!', '?', ':', '-', '(', ')', '[', ']', '{', '}' },
                                              StringSplitOptions.RemoveEmptyEntries);

                foreach (var sourceWord in sourceWords)
                {
                    var sourceStem = sourceWord;
                    foreach (var ending in wordEndings)
                    {
                        if (sourceWord.EndsWith(ending) && sourceWord.Length > ending.Length + 2)
                        {
                            sourceStem = sourceWord.Substring(0, sourceWord.Length - ending.Length);
                            break;
                        }
                    }

                    if (sourceStem == searchStem && searchStem.Length > 2)
                        return true;
                }

                return false;
            }



            // New method for award notices
            public async Task<PaginatedResponse<AwardNoticeDto>> ScrapeAwardNoticesAsync(int page = 1)
            {
                try
                {
                    _logger.LogInformation($"Scraping award notices page {page}");

                    var url = $"{BaseUrl}/egp-SW5kZXhlcy9nZXRBd2FyZE5vdGljZXM=";
                    if (page > 1)
                    {
                        url += $"?page={page}";
                    }

                    var html = await _httpClient.GetStringAsync(url);
                    var awardNotices = ParseAwardNoticesHtml(html, page);

                    var response = new PaginatedResponse<AwardNoticeDto>
                    {
                        Items = awardNotices,
                        PageNumber = page,
                        PageSize = 20, // Based on the HTML structure
                        TotalCount = await GetTotalAwardNoticesAsync(),
                        HasPreviousPage = page > 1,
                        HasNextPage = awardNotices.Count == 20
                    };

                    return response;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error scraping award notices page {page}");
                    throw;
                }
            }

            // New method for annual procurement plans
            public async Task<PaginatedResponse<AnnualProcurementPlanDto>> ScrapeAnnualProcurementPlansAsync(int page = 1)
            {
                try
                {
                    _logger.LogInformation($"Scraping annual procurement plans page {page}");

                    var url = $"{BaseUrl}/indexes/get-app?url=egp-SW5kZXhlcy9nZXRBcHA%3D";
                    if (page > 1)
                    {
                        url += $"&page={page}";
                    }

                    var html = await _httpClient.GetStringAsync(url);
                    var plans = ParseAnnualProcurementPlansHtml(html, page);

                    var response = new PaginatedResponse<AnnualProcurementPlanDto>
                    {
                        Items = plans,
                        PageNumber = page,
                        PageSize = 20, // Based on the HTML structure
                        TotalCount = await GetTotalAnnualProcurementPlansAsync(),
                        HasPreviousPage = page > 1,
                        HasNextPage = plans.Count == 20
                    };

                    return response;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error scraping annual procurement plans page {page}");
                    throw;
                }
            }

            // New method for past tenders
            public async Task<PaginatedResponse<Tender>> ScrapePastTendersAsync(int page = 1)
            {
                try
                {
                    _logger.LogInformation($"Scraping past tenders page {page}");

                    var url = $"{BaseUrl}/indexes/get-former-opportunities?url=egp-SW5kZXhlcy9nZXRGb3JtZXJPcHBvcnR1bml0aWVz";
                    if (page > 1)
                    {
                        url += $"&page={page}&direction=BulletinBoardConcluded.id";
                    }

                    var html = await _httpClient.GetStringAsync(url);
                    var pastTenders = ParsePastTendersHtml(html, page);

                    var response = new PaginatedResponse<Tender>
                    {
                        Items = pastTenders,
                        PageNumber = page,
                        PageSize = 20, // Based on the HTML structure
                        TotalCount = await GetTotalPastTendersAsync(),
                        HasPreviousPage = page > 1,
                        HasNextPage = pastTenders.Count == 20
                    };

                    return response;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error scraping past tenders page {page}");
                    throw;
                }
            }

            // Helper methods for parsing HTML
            private List<AwardNoticeDto> ParseAwardNoticesHtml(string html, int pageNumber)
            {
                var awardNotices = new List<AwardNoticeDto>();
                var htmlDocument = new HtmlDocument();
                htmlDocument.LoadHtml(html);

                var table = htmlDocument.DocumentNode.SelectSingleNode("//table[contains(@class, 'table-bordered')]");

                if (table == null)
                {
                    _logger.LogWarning($"No award notices table found on page {pageNumber}");
                    return awardNotices;
                }

                var rows = table.SelectNodes(".//tr[position()>1]");

                if (rows == null)
                {
                    _logger.LogWarning($"No award notice data rows found on page {pageNumber}");
                    return awardNotices;
                }

                foreach (var row in rows)
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells != null && cells.Count >= 5)
                    {
                        try
                        {
                            var awardNotice = new AwardNoticeDto
                            {
                                AwardNoticeNumber = GetCellText(cells[0]),
                                TenderId = GetCellText(cells[1]),
                                AwardTitle = GetCellText(cells[2]),
                                Awardee = GetCellText(cells[3]),
                                AwardDate = GetCellText(cells[4])
                            };

                            // Extract details URL if available
                            var linkNode = cells[0].SelectSingleNode(".//a");
                            if (linkNode != null)
                            {
                                var href = linkNode.GetAttributeValue("href", "");
                                awardNotice.DetailsUrl = href.StartsWith("http") ? href : BaseUrl + href;
                            }

                            awardNotices.Add(awardNotice);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error parsing award notice row");
                            continue;
                        }
                    }
                }

                return awardNotices;
            }

            private List<AnnualProcurementPlanDto> ParseAnnualProcurementPlansHtml(string html, int pageNumber)
            {
                var plans = new List<AnnualProcurementPlanDto>();
                var htmlDocument = new HtmlDocument();
                htmlDocument.LoadHtml(html);

                var table = htmlDocument.DocumentNode.SelectSingleNode("//table[contains(@class, 'table-bordered')]");

                if (table == null)
                {
                    _logger.LogWarning($"No annual procurement plans table found on page {pageNumber}");
                    return plans;
                }

                var rows = table.SelectNodes(".//tr[position()>1]");

                if (rows == null)
                {
                    _logger.LogWarning($"No annual procurement plan data rows found on page {pageNumber}");
                    return plans;
                }

                foreach (var row in rows)
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells != null && cells.Count >= 3)
                    {
                        try
                        {
                            var plan = new AnnualProcurementPlanDto
                            {
                                ProcuringEntity = GetCellText(cells[0]),
                                Year = GetCellText(cells[1])
                            };

                            // Extract view app URL if available
                            var linkNode = cells[2].SelectSingleNode(".//a");
                            if (linkNode != null)
                            {
                                var href = linkNode.GetAttributeValue("href", "");
                                plan.ViewAppUrl = href.StartsWith("http") ? href : BaseUrl + href;
                            }

                            plans.Add(plan);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error parsing annual procurement plan row");
                            continue;
                        }
                    }
                }

                return plans;
            }

            private List<Tender> ParsePastTendersHtml(string html, int pageNumber)
            {
                var pastTenders = new List<Tender>();
                var htmlDocument = new HtmlDocument();
                htmlDocument.LoadHtml(html);

                var table = htmlDocument.DocumentNode.SelectSingleNode("//table[contains(@class, 'table-bordered')]");

                if (table == null)
                {
                    _logger.LogWarning($"No past tenders table found on page {pageNumber}");
                    return pastTenders;
                }

                var rows = table.SelectNodes(".//tr[position()>1]");

                if (rows == null)
                {
                    _logger.LogWarning($"No past tender data rows found on page {pageNumber}");
                    return pastTenders;
                }

                foreach (var row in rows)
                {
                    var cells = row.SelectNodes(".//td");
                    if (cells != null && cells.Count >= 10)
                    {
                        try
                        {
                            var tender = new Tender
                            {
                                Id = Guid.NewGuid().ToString(),
                                PageNumber = pageNumber,
                                SourceUrl = $"{BaseUrl}/indexes/get-former-opportunities?url=egp-SW5kZXhlcy9nZXRGb3JtZXJPcHBvcnR1bml0aWVz&page={pageNumber}&direction=BulletinBoardConcluded.id"
                            };

                            // Tender ID and link
                            var idCell = cells[0];
                            var linkNode = idCell.SelectSingleNode(".//a");
                            if (linkNode != null)
                            {
                                tender.TenderId = linkNode.InnerText.Trim();
                                var href = linkNode.GetAttributeValue("href", "");
                                tender.DetailsUrl = href.StartsWith("http") ? href : BaseUrl + href;
                            }

                            // Reference Number
                            tender.ReferenceNumber = GetCellText(cells[1]);

                            // Title
                            tender.Title = GetCellText(cells[2]);

                            // Category Codes (split by comma)
                            var codesText = GetCellText(cells[3]);
                            tender.CategoryCodes = codesText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(c => c.Trim()).ToList();

                            // Category Names (split by comma)
                            var namesText = GetCellText(cells[4]);
                            tender.CategoryNames = namesText.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                .Select(c => c.Trim()).ToList();

                            // Procuring Entity
                            tender.ProcuringEntity = GetCellText(cells[5]);

                            // Parse dates
                            tender.PublishDate = ParseDate(GetCellText(cells[6]));
                            tender.ClosingDate = ParseDate(GetCellText(cells[7]));

                            // Status (for past tenders)
                            var status = GetCellText(cells[8]);
                            // You might want to add a Status field to the Tender model if needed

                            pastTenders.Add(tender);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error parsing past tender row");
                            continue;
                        }
                    }
                }

                return pastTenders;
            }

            // Helper methods for getting total counts
            private async Task<int> GetTotalAwardNoticesAsync()
            {
                // Implement logic to get total count of award notices
                // This might involve parsing a pagination element or making an additional request
                return 1000; // Placeholder
            }

            private async Task<int> GetTotalAnnualProcurementPlansAsync()
            {
                // Implement logic to get total count of annual procurement plans
                return 689; // Based on the HTML: "Page 2 of 35, showing 20 record(s) out of 689 total"
            }

            private async Task<int> GetTotalPastTendersAsync()
            {
                // Implement logic to get total count of past tenders
                return 38893; // Based on the HTML: "Page 2 of 1,945, showing 20 record(s) out of 38,893 total"
            }

            // Implement the search methods
            public async Task<PaginatedResponse<AwardNoticeDto>> SearchAwardNoticesAsync(AwardNoticeSearchRequest request)
            {
                // Build URL with search parameters
                var url = $"{BaseUrl}/egp-SW5kZXhlcy9nZXRBd2FyZE5vdGljZXM=";

                var queryParams = new List<string>();

                if (!string.IsNullOrEmpty(request.AwardNoticeNumber))
                    queryParams.Add($"searchAwardNoticeNo={Uri.EscapeDataString(request.AwardNoticeNumber)}");

                if (!string.IsNullOrEmpty(request.TenderId))
                    queryParams.Add($"searchRefNo={Uri.EscapeDataString(request.TenderId)}");

                if (!string.IsNullOrEmpty(request.AwardTitle))
                    queryParams.Add($"searchLineItem={Uri.EscapeDataString(request.AwardTitle)}");

                if (!string.IsNullOrEmpty(request.Awardee))
                    queryParams.Add($"searchAwardee={Uri.EscapeDataString(request.Awardee)}");

                if (request.AwardDateFrom.HasValue)
                    queryParams.Add($"searchAwardDateFrom={request.AwardDateFrom.Value:dd-MMM-yyyy}");

                if (request.AwardDateTo.HasValue)
                    queryParams.Add($"searchAwardDateTo={request.AwardDateTo.Value:dd-MMM-yyyy}");

                if (request.Page > 1)
                    queryParams.Add($"page={request.Page}");

                if (queryParams.Any())
                    url += "?" + string.Join("&", queryParams);

                var html = await _httpClient.GetStringAsync(url);
                var awardNotices = ParseAwardNoticesHtml(html, request.Page);

                return new PaginatedResponse<AwardNoticeDto>
                {
                    Items = awardNotices,
                    PageNumber = request.Page,
                    PageSize = request.PageSize,
                    TotalCount = await GetTotalAwardNoticesAsync(),
                    HasPreviousPage = request.Page > 1,
                    HasNextPage = awardNotices.Count == request.PageSize
                };
            }

            public async Task<PaginatedResponse<AnnualProcurementPlanDto>> SearchAnnualProcurementPlansAsync(AnnualProcurementPlanSearchRequest request)
            {
                // Build URL with search parameters
                var url = $"{BaseUrl}/indexes/get-app?url=egp-SW5kZXhlcy9nZXRBcHA%3D";

                var queryParams = new List<string>();

                if (!string.IsNullOrEmpty(request.ProcuringEntity))
                    queryParams.Add($"search_text={Uri.EscapeDataString(request.ProcuringEntity)}");

                if (!string.IsNullOrEmpty(request.Year))
                    queryParams.Add($"searchAnnualYear={Uri.EscapeDataString(request.Year)}");

                if (request.Page > 1)
                    queryParams.Add($"page={request.Page}");

                if (queryParams.Any())
                    url += "?" + string.Join("&", queryParams);

                var html = await _httpClient.GetStringAsync(url);
                var plans = ParseAnnualProcurementPlansHtml(html, request.Page);

                return new PaginatedResponse<AnnualProcurementPlanDto>
                {
                    Items = plans,
                    PageNumber = request.Page,
                    PageSize = request.PageSize,
                    TotalCount = await GetTotalAnnualProcurementPlansAsync(),
                    HasPreviousPage = request.Page > 1,
                    HasNextPage = plans.Count == request.PageSize
                };
            }

            public async Task<PaginatedResponse<Tender>> SearchPastTendersAsync(ScrapeRequest request)
            {
                try
                {
                    _logger.LogInformation($"Searching past tenders with filters");

                    // Check if we should use string matching search
                    if (!string.IsNullOrEmpty(request.SearchString))
                    {
                        return await SearchPastTendersByStringAsync(request);
                    }

                    // Existing filter-based search logic
                    var url = $"{BaseUrl}/indexes/get-former-opportunities?url=egp-SW5kZXhlcy9nZXRGb3JtZXJPcHBvcnR1bml0aWVz";

                    var queryParams = new List<string>();

                    // Add search parameters based on the ScrapeRequest properties
                    if (!string.IsNullOrEmpty(request.SearchRefNo))
                        queryParams.Add($"searchRefNo={Uri.EscapeDataString(request.SearchRefNo)}");

                    if (!string.IsNullOrEmpty(request.SearchtenderRefNo))
                        queryParams.Add($"searchtenderRefNo={Uri.EscapeDataString(request.SearchtenderRefNo)}");

                    if (!string.IsNullOrEmpty(request.SearchNoticeTitle))
                        queryParams.Add($"searchNoticeTitle={Uri.EscapeDataString(request.SearchNoticeTitle)}");

                    if (request.FilterCategories?.Any() == true)
                        queryParams.Add($"searchSuppName={Uri.EscapeDataString(string.Join(",", request.FilterCategories))}");

                    if (request.FilterEntities?.Any() == true)
                        queryParams.Add($"searchDept={Uri.EscapeDataString(string.Join(",", request.FilterEntities))}");

                    if (request.PublishDateFrom.HasValue)
                        queryParams.Add($"searchPublishDateFrom={request.PublishDateFrom.Value:dd-MMM-yyyy}");

                    if (request.PublishDateTo.HasValue)
                        queryParams.Add($"searchPublishDateTo={request.PublishDateTo.Value:dd-MMM-yyyy}");

                    if (request.ClosingDateFrom.HasValue)
                        queryParams.Add($"searchClosingDateFrom={request.ClosingDateFrom.Value:dd-MMM-yyyy}");

                    if (request.ClosingDateTo.HasValue)
                        queryParams.Add($"searchClosingDateTo={request.ClosingDateTo.Value:dd-MMM-yyyy}");

                    if (request.StartPage > 1)
                        queryParams.Add($"page={request.StartPage}");

                    if (queryParams.Any())
                        url += "?" + string.Join("&", queryParams);

                    var html = await _httpClient.GetStringAsync(url);
                    var pastTenders = ParsePastTendersHtml(html, request.StartPage);

                    return new PaginatedResponse<Tender>
                    {
                        Items = pastTenders,
                        PageNumber = request.StartPage,
                        PageSize = 20,
                        TotalCount = await GetTotalPastTendersAsync(),
                        HasPreviousPage = request.StartPage > 1,
                        HasNextPage = pastTenders.Count == 20
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error searching past tenders");
                    throw;
                }
            }

            // Add this new method for string-based search
            private async Task<PaginatedResponse<Tender>> SearchPastTendersByStringAsync(ScrapeRequest request)
            {
                try
                {
                    _logger.LogInformation($"String search for past tenders: '{request.SearchString}', page: {request.StartPage}");

                    // Get multiple pages to search through
                    var allPastTenders = new List<Tender>();
                    var pagesToSearch = Math.Min(request.MaxPagesToSearch, await GetTotalPastTendersPagesAsync());

                    // Start from the requested page or from page 1
                    var startPage = request.StartPage > 0 ? request.StartPage : 1;
                    var endPage = Math.Min(startPage + pagesToSearch - 1, await GetTotalPastTendersPagesAsync());

                    for (int p = startPage; p <= endPage; p++)
                    {
                        var pageResponse = await ScrapePastTendersAsync(p);
                        var matchingTenders = FilterAndRankPastTenders(pageResponse.Items, request);
                        allPastTenders.AddRange(matchingTenders);

                        // Break early if we have enough results
                        if (allPastTenders.Count >= 100) break;

                        await Task.Delay(300); // Respectful delay
                    }

                    // Apply ranking based on relevance if using advanced search
                    List<Tender> rankedResults;
                    if (request.UseAdvancedSearch)
                    {
                        var searchTerms = SplitSearchTermsAdvanced(request.SearchString);
                        rankedResults = allPastTenders.Select(t => new
                        {
                            Tender = t,
                            Score = CalculatePastTenderSearchScore(t, searchTerms, request)
                        })
                        .OrderByDescending(x => x.Score)
                        .Select(x => x.Tender)
                        .ToList();
                    }
                    else
                    {
                        rankedResults = allPastTenders;
                    }

                    // Apply pagination
                    var pageSize = 20;
                    var skipCount = (request.StartPage - 1) * pageSize;
                    var paginatedResults = rankedResults
                        .Skip(skipCount)
                        .Take(pageSize)
                        .ToList();

                    return new PaginatedResponse<Tender>
                    {
                        Items = paginatedResults,
                        PageNumber = request.StartPage,
                        PageSize = pageSize,
                        TotalCount = rankedResults.Count,
                        TotalPages = (int)Math.Ceiling(rankedResults.Count / (double)pageSize),
                        HasPreviousPage = request.StartPage > 1,
                        HasNextPage = skipCount + pageSize < rankedResults.Count
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error in string search for past tenders: '{request.SearchString}'");
                    throw;
                }
            }

            // Helper method to filter and rank past tenders
            private List<Tender> FilterAndRankPastTenders(List<Tender> pastTenders, ScrapeRequest request)
            {
                if (string.IsNullOrWhiteSpace(request.SearchString))
                    return pastTenders;

                var searchTerms = SplitSearchTermsAdvanced(request.SearchString);

                // Filter based on date ranges if specified
                var filteredTenders = pastTenders.Where(t =>
                {
                    // Apply date filters if specified
                    if (request.PublishDateFrom.HasValue && t.PublishDate.HasValue && t.PublishDate.Value < request.PublishDateFrom.Value)
                        return false;

                    if (request.PublishDateTo.HasValue && t.PublishDate.HasValue && t.PublishDate.Value > request.PublishDateTo.Value)
                        return false;

                    if (request.ClosingDateFrom.HasValue && t.ClosingDate.HasValue && t.ClosingDate.Value < request.ClosingDateFrom.Value)
                        return false;

                    if (request.ClosingDateTo.HasValue && t.ClosingDate.HasValue && t.ClosingDate.Value > request.ClosingDateTo.Value)
                        return false;

                    // Apply entity filters if specified
                    if (request.FilterEntities?.Any() == true &&
                        !request.FilterEntities.Any(e =>
                            Contains(t.ProcuringEntity, e)))
                        return false;

                    // Apply category filters if specified
                    if (request.FilterCategories?.Any() == true &&
                        !t.CategoryNames.Any(cn =>
                            request.FilterCategories.Any(fc => Contains(cn, fc))))
                        return false;

                    return true;
                }).ToList();

                // If not using advanced search, just do simple filtering
                if (!request.UseAdvancedSearch)
                {
                    return filteredTenders.Where(tender =>
                    {
                        var searchText = $"{tender.TenderId} {tender.ReferenceNumber} {tender.Title} {tender.ProcuringEntity} {tender.Scope}".ToLowerInvariant();
                        var searchTermsLower = searchTerms.Select(t => t.ToLowerInvariant()).ToList();

                        // Check if ALL search terms are found in the combined text
                        return searchTermsLower.All(term =>
                            searchText.Contains(term));
                    }).ToList();
                }

                return filteredTenders;
            }

            // Calculate search relevance score for past tenders
            private double CalculatePastTenderSearchScore(Tender tender, List<string> searchTerms, ScrapeRequest request)
            {
                double score = 0;

                var title = tender.Title ?? "";
                var entity = tender.ProcuringEntity ?? "";
                var scope = tender.Scope ?? "";
                var reference = tender.ReferenceNumber ?? "";
                var tenderId = tender.TenderId ?? "";
                var categories = string.Join(" ", tender.CategoryNames ?? new List<string>());

                // Check each search term
                foreach (var term in searchTerms)
                {
                    var termLower = term.ToLowerInvariant();

                    // Title matches get highest weight
                    if (title.ToLowerInvariant().Contains(termLower))
                        score += 3.0;

                    // Entity matches get high weight
                    if (entity.ToLowerInvariant().Contains(termLower))
                        score += 2.5;

                    // Reference number matches
                    if (reference.ToLowerInvariant().Contains(termLower))
                        score += 2.0;

                    // Tender ID matches
                    if (tenderId.ToLowerInvariant().Contains(termLower))
                        score += 2.0;

                    // Category matches
                    if (categories.ToLowerInvariant().Contains(termLower))
                        score += 1.5;

                    // Scope/content matches get medium weight
                    if (scope.ToLowerInvariant().Contains(termLower))
                        score += 1.0;

                    // Partial word matches
                    if (ContainsPartialMatch(title, term) || ContainsPartialMatch(entity, term))
                        score += 0.5;
                }

                // Bonus for exact phrase matches
                var fullSearchPhrase = string.Join(" ", searchTerms).ToLowerInvariant();
                if (title.ToLowerInvariant().Contains(fullSearchPhrase))
                    score += 5.0;

                // Apply additional scoring based on date relevance
                if (tender.ClosingDate.HasValue)
                {
                    // More recent tenders get higher score
                    var daysAgo = (DateTime.UtcNow - tender.ClosingDate.Value).TotalDays;
                    if (daysAgo <= 365) // Within the last year
                        score += 1.0;
                    if (daysAgo <= 180) // Within the last 6 months
                        score += 0.5;
                    if (daysAgo <= 90) // Within the last 3 months
                        score += 0.5;
                }

                return score;
            }

            // Helper method to get total past tender pages
            private async Task<int> GetTotalPastTendersPagesAsync()
            {
                var cacheKey = "TotalPastTenderPages";
                if (_cache.TryGetValue(cacheKey, out int cachedPages))
                    return cachedPages;

                try
                {
                    var firstPage = await ScrapePastTendersAsync(1);
                    var totalPages = (int)Math.Ceiling(firstPage.TotalCount / 20.0);

                    _cache.Set(cacheKey, totalPages, TimeSpan.FromMinutes(CacheDurationMinutes));
                    return totalPages;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error getting total past tender pages");
                    return 50; // Default fallback
                }
            }



            // Add the new methods for similarity analysis and company participation
            public async Task<List<TenderSimilarityDto>> AnalyzeTenderSimilarityAsync(string tenderId)
            {
                return await _similarityService.AnalyzeTenderSimilarityAsync(tenderId);
            }

            public async Task<List<TenderSimilarityDto>> FindSimilarTendersAsync(string title, int maxResults = 10)
            {
                return await _similarityService.FindSimilarTendersAsync(title, maxResults);
            }

            public async Task<List<TenderParticipationDto>> GetCompanyParticipationAsync(string companyName)
            {
                return await _similarityService.GetCompanyParticipationAsync(companyName);
            }

            public async Task<List<TenderParticipationDto>> GetTopParticipatingCompaniesAsync(int limit = 20)
            {
                return await _similarityService.GetTopParticipatingCompaniesAsync(limit);
            }

            // Add the similarity service as a private field
            private readonly TenderSimilarityService _similarityService;

            /// <summary>
            /// Scrape detailed annual procurement plan including all items
            /// </summary>
            public async Task<AnnualProcurementPlanDetailDto> GetAnnualProcurementPlanDetailAsync(string url)
            {
                try
                {
                    _logger.LogInformation($"Scraping procurement plan details from: {url}");

                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                    request.Headers.Referrer = new Uri("https://egp.praz.org.zw/indexes/get-app?url=egp-SW5kZXhlcy9nZXRBcHA%3D");

                    using var httpResponse = await _httpClient.SendAsync(request);
                    var html = await httpResponse.Content.ReadAsStringAsync();

                    var htmlDocument = new HtmlDocument();
                    htmlDocument.LoadHtml(html);

                    var detail = new AnnualProcurementPlanDetailDto
                    {
                        SourceUrl = url,
                        ScrapedAt = DateTime.UtcNow,
                        Items = new List<ProcurementPlanItem>()
                    };

                    // Extract title & entity from page
                    var titleNode = htmlDocument.DocumentNode.SelectSingleNode("//h1[@class='title'] | //h2[@class='title'] | //div[@class='page-header']//h1 | //div[@class='page-header']//h2 | //h2[contains(@class,'box-title')]");
                    if (titleNode != null)
                    {
                        detail.Title = titleNode.InnerText.Replace("View APP Details", "").Trim();
                        var titleParts = detail.Title.Split(new[] { '-' }, 2);
                        if (titleParts.Length >= 1)
                        {
                            detail.ProcuringEntity = titleParts[0].Trim();
                        }
                    }

                    // Extract year from URL or title
                    var urlParts = url.Split('/');
                    if (urlParts.Length > 1)
                    {
                        detail.Year = urlParts.LastOrDefault()?.Trim();
                    }

                    // Find the table with procurement plan items
                    var rows = htmlDocument.DocumentNode.SelectNodes("//tr[td]");
                    if (rows == null || rows.Count == 0)
                    {
                        _logger.LogWarning($"No procurement plan item rows found at {url}");
                        return detail;
                    }

                    foreach (var row in rows)
                    {
                        var cells = row.SelectNodes(".//td");
                        if (cells != null && cells.Count >= 5)
                        {
                            try
                            {
                                var item = new ProcurementPlanItem();

                                // Index mapping (matches PRAZ table layout exactly)
                                int colIndex = 0;
                                if (cells.Count > colIndex) item.ItemId = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.RefNo = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.ClassOfProcurement = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.ObjectCode = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.Description = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.PmoEndUser = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.ProcurementMethod = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.EoiPublicationDate = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.EoiClosingDate = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.TenderPublicationDate = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.BidClosingDate = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.AwardNoticeDate = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.ContractSigningDate = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.CycleDays = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.LeadTime = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.Spoc = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.SourceOfFunds = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.UnitOfMeasurement = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.Quantity = GetCellText(cells[colIndex++]);
                                if (cells.Count > colIndex) item.Comments = GetCellText(cells[colIndex++]);

                                // Ignore alert or header junk rows
                                if (!string.IsNullOrEmpty(item.Description) && !item.Description.Contains("nxt_alert_box") && item.Description.Length > 2)
                                {
                                    detail.Items.Add(item);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Error parsing procurement plan item row");
                                continue;
                            }
                        }
                    }

                    detail.TotalItems = detail.Items.Count;
                    _logger.LogInformation($"Scraped {detail.TotalItems} items from procurement plan: {url}");
                    return detail;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error scraping procurement plan detail from {url}");
                    throw;
                }
            }

            private string? _prazSessionCookie;

            public void SetPrazSessionCookie(string cookie)
            {
                _prazSessionCookie = cookie?.Trim();
            }

            public async Task<ZimbabweTenderDetailDto> GetZimbabweTenderDetailsAsync(string tenderId)
            {
                if (string.IsNullOrWhiteSpace(tenderId))
                {
                    throw new ArgumentException("TenderId cannot be empty.", nameof(tenderId));
                }

                var cleanId = tenderId.Trim();
                var cacheKey = $"zimbabwe_tender_detail_{cleanId}";
                if (_cache.TryGetValue(cacheKey, out ZimbabweTenderDetailDto? cached) && cached != null)
                {
                    return cached;
                }

                try
                {
                    var url = $"{BaseUrl}/Indexes/viewLiveTenderDetails/{cleanId}";
                    _logger.LogInformation("Scraping Zimbabwe live tender details from {Url}", url);

                    var response = await _httpClient.GetAsync(url);
                    response.EnsureSuccessStatusCode();

                    var html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var detail = new ZimbabweTenderDetailDto
                    {
                        TenderId = cleanId,
                        DetailsUrl = url,
                        ScrapedAt = DateTime.UtcNow
                    };

                    // Header Tender Id & Status
                    var tenderIdLabel = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'headerFont')]//label[contains(text(), 'Tender Id')]");
                    if (tenderIdLabel != null)
                    {
                        var rawId = tenderIdLabel.InnerText.Replace("Tender Id", "").Replace(":", "").Trim();
                        if (!string.IsNullOrEmpty(rawId)) detail.TenderId = rawId;
                    }

                    var statusDiv = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'headerFont')]//div[contains(., 'Status')]");
                    if (statusDiv != null)
                    {
                        detail.Status = CleanText(statusDiv.InnerText.Replace("Status", "").Replace(":", "").Trim());
                    }

                    // Extract all labels from left and right tamoha_twelvepx divs
                    var tamohaDivs = doc.DocumentNode.SelectNodes("//div[@id='block_content']//div[contains(@class, 'tamoha_twelvepx')]");
                    var leftCol = tamohaDivs != null && tamohaDivs.Count > 0 ? tamohaDivs[0] : null;
                    var rightCol = tamohaDivs != null && tamohaDivs.Count > 1 ? tamohaDivs[1] : null;

                    // Left Column
                    if (leftCol != null)
                    {
                        var labels = leftCol.SelectNodes(".//label");
                        if (labels != null)
                        {
                            foreach (var lbl in labels)
                            {
                                var key = CleanText(lbl.InnerText).TrimEnd(':');
                                var val = ExtractLabelValue(lbl);
                                if (!string.IsNullOrEmpty(key))
                                {
                                    detail.RawFields[key] = val;
                                    var keyLower = key.ToLowerInvariant();
                                    if (keyLower.Contains("bid validity period")) detail.BidValidityPeriod = val;
                                    else if (keyLower.Contains("tender reference number")) detail.TenderReferenceNumber = val;
                                    else if (keyLower.Contains("lot type")) detail.LotType = val;
                                    else if (keyLower.Contains("procurement method")) detail.ProcurementMethod = val;
                                    else if (keyLower.Contains("class of procurement")) detail.ClassOfProcurement = val;
                                    else if (keyLower.Contains("applicable procurement rules")) detail.ApplicableProcurementRules = val;
                                    else if (keyLower.Contains("funding source")) detail.FundingSource = val;
                                    else if (keyLower.Contains("delivery period")) detail.DeliveryPeriod = val;
                                    else if (keyLower.Contains("delivery/project location") || keyLower.Contains("delivery")) detail.DeliveryProjectLocation = val;
                                    else if (keyLower.Contains("required supplier categories")) detail.RequiredSupplierCategories = val;
                                    else if (keyLower.Contains("procuring entity")) detail.ProcuringEntity = val;
                                    else if (keyLower.Contains("date created")) detail.DateCreated = ParseDate(val);
                                }
                            }
                        }
                    }

                    // Middle Column (Entity, Address, Project Name, Description, Line Items)
                    var midCol = doc.DocumentNode.SelectSingleNode("//div[@id='block_content']//div[contains(@class, 'col-sm-6')]");
                    if (midCol != null)
                    {
                        var entityCenter = midCol.SelectSingleNode(".//center[contains(@class, 'verdhana_twelvepx')][1]");
                        if (entityCenter != null && string.IsNullOrEmpty(detail.ProcuringEntity))
                        {
                            detail.ProcuringEntity = CleanText(entityCenter.InnerText);
                        }

                        var addrNodes = midCol.SelectNodes(".//center[contains(@class, 'verdhana_twelvepx')]");
                        if (addrNodes != null && addrNodes.Count > 1)
                        {
                            var addrList = addrNodes.Skip(1).Select(a => CleanText(a.InnerText)).Where(s => !string.IsNullOrEmpty(s)).ToList();
                            detail.ProcuringEntityAddress = string.Join(", ", addrList);
                        }
                    }

                    // Extract Project Name
                    var pnMatch = System.Text.RegularExpressions.Regex.Match(html, @"<b>\s*Project Name:\s*</b>\s*(?:</br>|<br/?>)?\s*([^<\r\n]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (pnMatch.Success)
                    {
                        detail.ProjectName = CleanText(pnMatch.Groups[1].Value);
                    }
                    else
                    {
                        var wrappedTitle = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'wrapped-long-string')][1]");
                        if (wrappedTitle != null) detail.ProjectName = CleanText(wrappedTitle.InnerText);
                    }

                    // Extract Description
                    var descMatch = System.Text.RegularExpressions.Regex.Match(html, @"<b>\s*Description:\s*</b>\s*(?:</br>|<br/?>)?\s*<div[^>]*>(?:<p>)?([\s\S]*?)(?:</p>)?</div>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (descMatch.Success)
                    {
                        detail.Description = CleanText(descMatch.Groups[1].Value);
                    }

                    // Extract Line Items
                    var tableMatch = System.Text.RegularExpressions.Regex.Match(html, @"<table[^>]*fixed_header[^>]*>([\s\S]*?)</table>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (tableMatch.Success)
                    {
                        var rowMatches = System.Text.RegularExpressions.Regex.Matches(tableMatch.Groups[1].Value, @"<tr>([\s\S]*?)</tr>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        foreach (System.Text.RegularExpressions.Match rMatch in rowMatches)
                        {
                            var tdMatches = System.Text.RegularExpressions.Regex.Matches(rMatch.Groups[1].Value, @"<td[^>]*>([\s\S]*?)</td>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            if (tdMatches.Count >= 5)
                            {
                                var cleanTds = tdMatches.Select(td => CleanText(td.Groups[1].Value)).ToList();
                                detail.LineItems.Add(new ZimbabweLineItemDto
                                {
                                    ItemNumber = cleanTds[0],
                                    Unspsc = cleanTds[1],
                                    LotName = cleanTds[2],
                                    LotDescription = cleanTds[3],
                                    Quantity = cleanTds[4],
                                    UnitOfMeasure = cleanTds.Count > 5 ? cleanTds[5] : ""
                                });
                            }
                        }
                    }

                    // Right Column
                    if (rightCol != null)
                    {
                        var labels = rightCol.SelectNodes(".//label");
                        if (labels != null)
                        {
                            foreach (var lbl in labels)
                            {
                                var key = CleanText(lbl.InnerText).TrimEnd(':');
                                var val = ExtractLabelValue(lbl);
                                if (!string.IsNullOrEmpty(key))
                                {
                                    detail.RawFields[key] = val;
                                    var keyLower = key.ToLowerInvariant();
                                    if (keyLower.Contains("published date")) detail.PublishedDate = ParseDate(val);
                                    else if (keyLower.Contains("closing date")) detail.ClosingDate = ParseDate(val);
                                    else if (keyLower.Contains("date last updated")) detail.DateLastUpdated = ParseDate(val);
                                    else if (keyLower.Contains("bid form fee")) detail.BidFormFee = val;
                                    else if (keyLower.Contains("bid security amount(domestic)")) detail.BidSecurityDomestic = val;
                                    else if (keyLower.Contains("bid security amount(international)")) detail.BidSecurityInternational = val;
                                    else if (keyLower.Contains("establishment amount(domestic)")) detail.EstablishmentAmountDomestic = val;
                                    else if (keyLower.Contains("establishment amount(international)")) detail.EstablishmentAmountInternational = val;
                                    else if (keyLower.Contains("spoc fee")) detail.SpocFee = val;
                                    else if (keyLower.Contains("number of downloads"))
                                    {
                                        if (int.TryParse(val, out int dl)) detail.NumberOfDownloads = dl;
                                    }
                                }
                            }
                        }

                        var addendumAnchor = rightCol.SelectSingleNode(".//a[contains(@href, 'tender_corr_details')]");
                        if (addendumAnchor != null && int.TryParse(CleanText(addendumAnchor.InnerText), out int addendums))
                        {
                            detail.TenderAddendums = addendums;
                        }

                        var docPreviewAnchor = rightCol.SelectSingleNode(".//a[contains(@href_path, 'tender_doc_view')]");
                        if (docPreviewAnchor != null)
                        {
                            var path = docPreviewAnchor.GetAttributeValue("href_path", "");
                            detail.DocumentsPreviewUrl = path.StartsWith("http") ? path : $"{BaseUrl}{path}";
                        }
                    }

                    try
                    {
                        detail.Documents = await GetZimbabweTenderDocumentsAsync(cleanId, _prazSessionCookie);
                    }
                    catch (Exception docEx)
                    {
                        _logger.LogWarning(docEx, "Could not fetch tender documents for {TenderId}", cleanId);
                    }

                    _cache.Set(cacheKey, detail, TimeSpan.FromMinutes(10));
                    return detail;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching Zimbabwe tender details for {TenderId}", cleanId);
                    throw;
                }
            }

            public async Task<List<ZimbabweTenderDocumentDto>> GetZimbabweTenderDocumentsAsync(string tenderId, string? sessionCookie = null)
            {
                if (string.IsNullOrWhiteSpace(tenderId))
                {
                    throw new ArgumentException("TenderId cannot be empty.", nameof(tenderId));
                }

                var cleanId = tenderId.Trim();
                var cookieToUse = sessionCookie ?? _prazSessionCookie;

                try
                {
                    var url = $"{BaseUrl}/Tenders/tender_doc_view/{cleanId}/{cleanId}";
                    _logger.LogInformation("Fetching Zimbabwe tender documents from {Url}", url);

                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                    if (!string.IsNullOrEmpty(cookieToUse))
                    {
                        request.Headers.Add("Cookie", cookieToUse);
                    }

                    var response = await _httpClient.SendAsync(request);
                    var html = await response.Content.ReadAsStringAsync();

                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var documents = new List<ZimbabweTenderDocumentDto>();

                    if (html.Contains("User Session Expiring") || html.Contains("Your session has been expired"))
                    {
                        _logger.LogWarning("PRAZ document view returned session expired. Active session cookie required for downloading files.");
                        return documents;
                    }

                    var docRows = doc.DocumentNode.SelectNodes("//table//tr[td]");
                    if (docRows != null)
                    {
                        int docIndex = 1;
                        foreach (var row in docRows)
                        {
                            var cells = row.SelectNodes("td");
                            if (cells == null || cells.Count < 2) continue;

                            var link = row.SelectSingleNode(".//a[contains(@href, 'download') or contains(@href, 'document')]");
                            var fileName = CleanText(link?.InnerText ?? cells[0].InnerText);
                            var href = link?.GetAttributeValue("href", "") ?? "";

                            documents.Add(new ZimbabweTenderDocumentDto
                            {
                                DocumentId = docIndex.ToString(),
                                TenderId = cleanId,
                                Title = fileName,
                                FileName = fileName,
                                DownloadUrl = href.StartsWith("http") ? href : $"{BaseUrl}{href}"
                            });
                            docIndex++;
                        }
                    }

                    return documents;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching Zimbabwe tender documents for {TenderId}", cleanId);
                    throw;
                }
            }

            private static string ExtractLabelValue(HtmlNode lbl)
            {
                var nextNode = lbl.NextSibling;
                var valBuilder = new System.Text.StringBuilder();
                while (nextNode != null && nextNode.Name != "label")
                {
                    if (nextNode.NodeType == HtmlNodeType.Text)
                    {
                        valBuilder.Append(nextNode.InnerText).Append(" ");
                    }
                    nextNode = nextNode.NextSibling;
                }
                return CleanText(valBuilder.ToString());
            }

            private static string CleanText(string? input)
            {
                if (string.IsNullOrWhiteSpace(input)) return string.Empty;
                var decoded = System.Net.WebUtility.HtmlDecode(input);
                return System.Text.RegularExpressions.Regex.Replace(decoded, @"\s+", " ").Trim();
            }
    }
}