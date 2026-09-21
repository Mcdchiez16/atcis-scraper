using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ZimbabweTenderAPI.Models;

namespace ZimbabweTenderAPI.Services
{
    public class ZambiaTenderScraperService : IZambiaTenderScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ZambiaTenderScraperService> _logger;

        private const string BaseUrl = "https://eprocure.zppa.org.zm";
        private const string OpenedTendersPath = "/epps/common/viewOpenedTenders.do";
        private const string ViewPublicationPath = "/epps/app/viewPublication.do";
        private const string ViewTenderDetailsPath = "/epps/cft/prepareViewCfTWS.do";
        private const string ListContractDocumentsPath = "/epps/cft/listContractDocuments.do";
        private const string DownloadContractDocumentPath = "/epps/cft/downloadContractDocument.do";
        private const string CacheKeyTotalPages = "zambia_total_pages";
        private const int CacheDurationMinutes = 10;

        public ZambiaTenderScraperService(
            HttpClient httpClient,
            IMemoryCache cache,
            IConfiguration configuration,
            ILogger<ZambiaTenderScraperService> logger)
        {
            _httpClient = httpClient;
            _httpClient.Timeout = TimeSpan.FromSeconds(45);
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
        }

        public async Task<ZambiaTenderBatch> ScrapeOpenedTendersPageAsync(int pageNumber)
        {
            try
            {
                var cacheKey = $"zambia_page_{pageNumber}";
                if (_cache.TryGetValue(cacheKey, out ZambiaTenderBatch? cachedBatch) && cachedBatch != null)
                {
                    _logger.LogInformation("Returning cached Zambia tenders page {Page}", pageNumber);
                    return cachedBatch;
                }

                _logger.LogInformation("Scraping Zambia tenders page {Page}", pageNumber);

                var url = $"{BaseUrl}{OpenedTendersPath}?d-3680181-p={pageNumber}";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync();
                var batch = ParseOpenedTendersHtml(html, pageNumber, url);

                int totalPages = await GetTotalPagesFromHtmlOrCacheAsync(html);
                batch.TotalPages = totalPages;
                batch.HasMorePages = pageNumber < totalPages;
                batch.NextPage = batch.HasMorePages ? pageNumber + 1 : null;

                _cache.Set(cacheKey, batch, TimeSpan.FromMinutes(CacheDurationMinutes));

                return batch;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping Zambia tenders page {Page}", pageNumber);
                throw;
            }
        }

        public async Task<List<ZambiaTender>> ScrapeMultipleOpenedTendersPagesAsync(int startPage, int endPage)
        {
            if (startPage < 1) startPage = 1;
            if (endPage < startPage) endPage = startPage;

            var results = new ConcurrentBag<ZambiaTender>();
            var tasks = new List<Task>();

            using var semaphore = new System.Threading.SemaphoreSlim(4);

            for (int p = startPage; p <= endPage; p++)
            {
                int currentPage = p;
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        var batch = await ScrapeOpenedTendersPageAsync(currentPage);
                        foreach (var tender in batch.Tenders)
                        {
                            results.Add(tender);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to scrape Zambia page {Page}", currentPage);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            return results
                .OrderBy(t => t.PageNumber)
                .ThenBy(t => t.Title)
                .ToList();
        }

        public async Task<int> GetTotalPagesAsync()
        {
            if (_cache.TryGetValue(CacheKeyTotalPages, out int totalPages) && totalPages > 0)
            {
                return totalPages;
            }

            try
            {
                var url = $"{BaseUrl}{OpenedTendersPath}?d-3680181-p=1";
                var html = await _httpClient.GetStringAsync(url);
                return await GetTotalPagesFromHtmlOrCacheAsync(html);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Zambia total pages");
                return 1;
            }
        }

        public async Task<ZambiaTenderBatch> SearchOpenedTendersAsync(string keyword, int page = 1, int maxPagesToScan = 5)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return await ScrapeOpenedTendersPageAsync(page);
            }

            var allTenders = await ScrapeMultipleOpenedTendersPagesAsync(1, maxPagesToScan);
            var cleanKeyword = keyword.Trim().ToLowerInvariant();

            var filtered = allTenders.Where(t =>
                (!string.IsNullOrEmpty(t.Title) && t.Title.ToLowerInvariant().Contains(cleanKeyword)) ||
                (!string.IsNullOrEmpty(t.ReferenceNumber) && t.ReferenceNumber.ToLowerInvariant().Contains(cleanKeyword)) ||
                (!string.IsNullOrEmpty(t.ProcuringEntity) && t.ProcuringEntity.ToLowerInvariant().Contains(cleanKeyword)) ||
                (!string.IsNullOrEmpty(t.ProcurementMethod) && t.ProcurementMethod.ToLowerInvariant().Contains(cleanKeyword)) ||
                (!string.IsNullOrEmpty(t.Status) && t.Status.ToLowerInvariant().Contains(cleanKeyword))
            ).ToList();

            const int pageSize = 20;
            var pagedResults = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return new ZambiaTenderBatch
            {
                Tenders = pagedResults,
                Page = page,
                TotalTenders = filtered.Count,
                TotalPages = (int)Math.Ceiling((double)filtered.Count / pageSize),
                HasMorePages = page * pageSize < filtered.Count,
                NextPage = page * pageSize < filtered.Count ? page + 1 : null,
                ScrapedAt = DateTime.UtcNow
            };
        }

        public async Task<List<ZambiaTender>> GetTendersByEntityAsync(string entityName, int maxPagesToScan = 5)
        {
            if (string.IsNullOrWhiteSpace(entityName))
                return new List<ZambiaTender>();

            var allTenders = await ScrapeMultipleOpenedTendersPagesAsync(1, maxPagesToScan);
            var cleanEntity = entityName.Trim().ToLowerInvariant();

            return allTenders.Where(t =>
                !string.IsNullOrEmpty(t.ProcuringEntity) &&
                t.ProcuringEntity.ToLowerInvariant().Contains(cleanEntity)
            ).ToList();
        }

        public async Task<ZambiaPublicationBatch> ScrapePublishedProcurementPlansAsync(int page = 1, string? cycleId = null)
        {
            try
            {
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(cycleId)) queryParams.Add($"cycleId={cycleId}");
                queryParams.Add($"d-148516-p={page}");

                var url = $"{BaseUrl}{ViewPublicationPath}?{string.Join("&", queryParams)}";
                _logger.LogInformation("Scraping published procurement plans from {Url}", url);

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var batch = new ZambiaPublicationBatch
                {
                    Page = page,
                    ScrapedAt = DateTime.UtcNow
                };

                var cycleIdNode = doc.DocumentNode.SelectSingleNode("//dl//dt[contains(text(),'Cycle ID')]/following-sibling::dd[1]");
                batch.CycleId = CleanText(cycleIdNode?.InnerText) ?? cycleId ?? "";

                var descNode = doc.DocumentNode.SelectSingleNode("//dl//dt[contains(text(),'Description')]/following-sibling::dd[1]");
                batch.CycleDescription = CleanText(descNode?.InnerText);

                var rows = doc.DocumentNode.SelectNodes("//table[@id='T02']//tbody//tr");
                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        var cells = row.SelectNodes("td");
                        if (cells == null || cells.Count < 3) continue;

                        try
                        {
                            var orgAnchor = cells[0].SelectSingleNode(".//a");
                            var orgName = CleanText(orgAnchor?.InnerText ?? cells[0].InnerText);
                            var orgHref = orgAnchor?.GetAttributeValue("href", "") ?? "";
                            var orgIdMatch = Regex.Match(orgHref, @"id=(\d+)");
                            var orgId = orgIdMatch.Success ? orgIdMatch.Groups[1].Value : "";

                            var pubDateStr = CleanText(cells[1].InnerText);
                            var pubDate = ParseZambiaDate(pubDateStr);

                            var downloadAnchor = cells[2].SelectSingleNode(".//a");
                            var downloadHref = downloadAnchor?.GetAttributeValue("href", "") ?? "";
                            var downloadUrl = MakeAbsoluteUrl(downloadHref);
                            var subIdMatch = Regex.Match(downloadHref, @"submissionId=(\d+)");
                            var submissionId = subIdMatch.Success ? subIdMatch.Groups[1].Value : "";

                            batch.Items.Add(new ZambiaPublicationPlanDto
                            {
                                OrganisationName = orgName,
                                OrganisationId = orgId,
                                PublishedAt = pubDate,
                                DownloadUrl = downloadUrl,
                                SubmissionId = submissionId,
                                CycleId = batch.CycleId
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error parsing publication plan row");
                        }
                    }
                }

                batch.TotalItems = batch.Items.Count;

                var lastNav = doc.DocumentNode.SelectSingleNode("//table[@id='T02']/preceding::*[@id='lastNav'][1]");
                var href = lastNav?.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(href))
                {
                    var match = Regex.Match(href, @"[?&]d-148516-p=(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int total))
                    {
                        batch.TotalPages = total;
                        batch.HasMorePages = page < total;
                        batch.NextPage = batch.HasMorePages ? page + 1 : null;
                    }
                }

                if (batch.TotalPages == 0) batch.TotalPages = 1;

                return batch;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error scraping published procurement plans");
                throw;
            }
        }

        public async Task<ZambiaTenderDetailDto> GetTenderDetailsAsync(string resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new ArgumentException("ResourceId cannot be empty", nameof(resourceId));
            }

            var cleanId = resourceId.Trim();
            var cacheKey = $"zambia_detail_{cleanId}";
            if (_cache.TryGetValue(cacheKey, out ZambiaTenderDetailDto? cachedDetail) && cachedDetail != null)
            {
                return cachedDetail;
            }

            try
            {
                var url = $"{BaseUrl}{ViewTenderDetailsPath}?resourceId={cleanId}";
                _logger.LogInformation("Scraping Zambia tender details from {Url}", url);

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var detail = new ZambiaTenderDetailDto
                {
                    ResourceId = cleanId,
                    DetailsUrl = url,
                    ScrapedAt = DateTime.UtcNow
                };

                // Header Title
                var headerSpan = doc.DocumentNode.SelectSingleNode("//h2//strong");
                detail.Title = CleanText(headerSpan?.InnerText);

                // Parse key-value definition list: <dt> and <dd>
                var dtNodes = doc.DocumentNode.SelectNodes("//dl/dt");
                if (dtNodes != null)
                {
                    foreach (var dt in dtNodes)
                    {
                        var key = CleanText(dt.InnerText).TrimEnd(':');
                        var dd = dt.SelectSingleNode("following-sibling::dd[1]");
                        var val = CleanText(dd?.InnerText);

                        if (!string.IsNullOrEmpty(key))
                        {
                            detail.RawFields[key] = val;

                            var keyLower = key.ToLowerInvariant();
                            if (keyLower.Contains("name of procuring entity"))
                            {
                                detail.ProcuringEntity = val;
                                var orgAnchor = dd?.SelectSingleNode(".//a");
                                var orgHref = orgAnchor?.GetAttributeValue("href", "") ?? "";
                                var orgMatch = Regex.Match(orgHref, @"id=(\d+)");
                                if (orgMatch.Success) detail.ProcuringEntityId = orgMatch.Groups[1].Value;
                            }
                            else if (keyLower.Contains("app reference number"))
                            {
                                detail.AppReferenceNumber = val;
                            }
                            else if (keyLower.Contains("tender unique id"))
                            {
                                detail.TenderUniqueId = val;
                            }
                            else if (keyLower.Contains("title") && string.IsNullOrEmpty(detail.Title))
                            {
                                detail.Title = val;
                            }
                            else if (keyLower.Contains("description"))
                            {
                                detail.Description = val;
                            }
                            else if (keyLower.Contains("bid submission deadline in"))
                            {
                                detail.DeadlineRemaining = val;
                            }
                            else if (keyLower.Contains("deadline for bid submission"))
                            {
                                detail.SubmissionDeadline = ParseZambiaDate(val);
                            }
                            else if (keyLower.Contains("bid opening date"))
                            {
                                detail.BidOpeningDate = ParseZambiaDate(val);
                            }
                            else if (keyLower.Contains("end of clarification period"))
                            {
                                detail.ClarificationDeadline = ParseZambiaDate(val);
                            }
                            else if (keyLower.Contains("date of publication/invitation"))
                            {
                                detail.PublicationDate = ParseZambiaDate(val);
                            }
                            else if (keyLower.Contains("contract notice date"))
                            {
                                detail.ContractNoticeDate = ParseZambiaDate(val);
                            }
                            else if (keyLower.Contains("procurement type"))
                            {
                                detail.ProcurementType = val;
                            }
                            else if (keyLower.Contains("procedure"))
                            {
                                detail.Procedure = val;
                            }
                            else if (keyLower.Contains("commencement type"))
                            {
                                detail.CommencementType = val;
                            }
                            else if (keyLower.Contains("threshold"))
                            {
                                detail.Threshold = val;
                            }
                            else if (keyLower.Contains("procurement technique"))
                            {
                                detail.ProcurementTechnique = val;
                            }
                            else if (keyLower.Contains("number of stages"))
                            {
                                if (int.TryParse(val, out int stages)) detail.NumberOfStages = stages;
                            }
                            else if (keyLower.Contains("evaluation mechanism"))
                            {
                                detail.EvaluationMechanism = val;
                            }
                            else if (keyLower.Contains("ceec preference type"))
                            {
                                detail.CeecPreferenceType = val;
                            }
                            else if (keyLower.Contains("framework agreement"))
                            {
                                detail.FrameworkAgreement = val.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                            }
                            else if (keyLower.Contains("postqualification"))
                            {
                                detail.Postqualification = val.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                            }
                            else if (keyLower.Contains("payment type"))
                            {
                                detail.PaymentType = val;
                            }
                            else if (keyLower.Contains("payment amount"))
                            {
                                detail.PaymentAmount = val;
                            }
                            else if (keyLower.Contains("payment terms"))
                            {
                                detail.PaymentTerms = val;
                            }
                            else if (keyLower.Contains("bid security type"))
                            {
                                detail.BidSecurityType = val;
                            }
                            else if (keyLower.Contains("contract awarded in lots"))
                            {
                                detail.AwardedInLots = val.Equals("Yes", StringComparison.OrdinalIgnoreCase);
                            }
                            else if (keyLower.Contains("lot name"))
                            {
                                if (!string.IsNullOrEmpty(val) && !detail.Lots.Contains(val))
                                {
                                    detail.Lots.Add(val);
                                }
                            }
                            else if (keyLower.Contains("unspsc codes"))
                            {
                                if (!string.IsNullOrEmpty(val))
                                {
                                    detail.UnspscCodes.Add(val);
                                }
                            }
                        }
                    }
                }

                // Automatically fetch attached documents list
                try
                {
                    detail.Documents = await GetTenderDocumentsAsync(cleanId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch attached documents for tender {ResourceId}", cleanId);
                }

                _cache.Set(cacheKey, detail, TimeSpan.FromMinutes(CacheDurationMinutes));
                return detail;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting tender details for resource {ResourceId}", cleanId);
                throw;
            }
        }

        public async Task<List<ZambiaTenderDocumentDto>> GetTenderDocumentsAsync(string resourceId)
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                throw new ArgumentException("ResourceId cannot be empty", nameof(resourceId));
            }

            var cleanId = resourceId.Trim();
            var cacheKey = $"zambia_docs_{cleanId}";
            if (_cache.TryGetValue(cacheKey, out List<ZambiaTenderDocumentDto>? cachedDocs) && cachedDocs != null)
            {
                return cachedDocs;
            }

            try
            {
                var url = $"{BaseUrl}{ListContractDocumentsPath}?resourceId={cleanId}";
                _logger.LogInformation("Scraping attached tender documents from {Url}", url);

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var html = await response.Content.ReadAsStringAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var documents = new List<ZambiaTenderDocumentDto>();

                // Table T02 on listContractDocuments.do contains contract documents
                var rows = doc.DocumentNode.SelectNodes("//table[@id='T02']//tbody//tr");
                if (rows != null)
                {
                    foreach (var row in rows)
                    {
                        var cells = row.SelectNodes("td");
                        if (cells == null || cells.Count < 3) continue;

                        try
                        {
                            var addendumId = CleanText(cells[0].InnerText);
                            var title = CleanText(cells[1].InnerText);

                            var fileAnchor = cells[2].SelectSingleNode(".//a");
                            var fileName = CleanText(fileAnchor?.InnerText ?? cells[2].InnerText);

                            // Extract documentId from onclick or href
                            var onclick = fileAnchor?.GetAttributeValue("onclick", "") ?? "";
                            var docIdMatch = Regex.Match(onclick, @"downloadDocForAnonymous\('(\d+)'\)");
                            if (!docIdMatch.Success)
                            {
                                docIdMatch = Regex.Match(onclick, @"documentId=(\d+)");
                            }

                            var documentId = docIdMatch.Success ? docIdMatch.Groups[1].Value : "";

                            var desc = cells.Count > 3 ? CleanText(cells[3].InnerText) : "";
                            var lang = cells.Count > 4 ? CleanText(cells[4].InnerText) : "EN";

                            var downloadUrl = !string.IsNullOrEmpty(documentId)
                                ? $"{BaseUrl}{DownloadContractDocumentPath}?documentId={documentId}&resourceId={cleanId}"
                                : "";

                            documents.Add(new ZambiaTenderDocumentDto
                            {
                                DocumentId = documentId,
                                ResourceId = cleanId,
                                AddendumId = addendumId,
                                Title = title,
                                FileName = fileName,
                                Description = desc,
                                Language = lang,
                                DownloadUrl = downloadUrl
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error parsing tender document row");
                        }
                    }
                }

                _cache.Set(cacheKey, documents, TimeSpan.FromMinutes(CacheDurationMinutes));
                return documents;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting attached documents for resource {ResourceId}", cleanId);
                throw;
            }
        }

        public async Task<(byte[] FileBytes, string ContentType, string FileName)> DownloadDocumentFileAsync(string fileUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl))
            {
                throw new ArgumentException("File URL cannot be empty.", nameof(fileUrl));
            }

            var absoluteUrl = MakeAbsoluteUrl(fileUrl);

            var cacheDir = Path.Combine(AppContext.BaseDirectory, "uploads", "zambia", "cache");
            Directory.CreateDirectory(cacheDir);

            var urlHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(absoluteUrl)))[..16];
            var cachedMetadataPath = Path.Combine(cacheDir, $"{urlHash}.meta.json");
            var cachedFilePath = Path.Combine(cacheDir, $"{urlHash}.bin");

            if (File.Exists(cachedMetadataPath) && File.Exists(cachedFilePath))
            {
                try
                {
                    var metaJson = await File.ReadAllTextAsync(cachedMetadataPath);
                    var meta = JsonSerializer.Deserialize<Dictionary<string, string>>(metaJson);
                    var bytes = await File.ReadAllBytesAsync(cachedFilePath);
                    return (bytes, meta?["ContentType"] ?? "application/octet-stream", meta?["FileName"] ?? "download.bin");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed reading cached file, re-downloading from source");
                }
            }

            _logger.LogInformation("Downloading remote document from {Url}", absoluteUrl);
            var response = await _httpClient.GetAsync(absoluteUrl);
            response.EnsureSuccessStatusCode();

            var fileBytes = await response.Content.ReadAsByteArrayAsync();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

            var fileName = response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
                ?? response.Content.Headers.ContentDisposition?.FileNameStar?.Trim('"');

            if (string.IsNullOrWhiteSpace(fileName))
            {
                if (absoluteUrl.Contains("submissionId="))
                {
                    var match = Regex.Match(absoluteUrl, @"submissionId=(\d+)");
                    fileName = $"plan_submission_{match.Groups[1].Value}.xls";
                }
                else if (contentType.Contains("pdf", StringComparison.OrdinalIgnoreCase))
                {
                    fileName = $"zambia_document_{urlHash}.pdf";
                }
                else
                {
                    fileName = $"zambia_file_{urlHash}.bin";
                }
            }

            try
            {
                await File.WriteAllBytesAsync(cachedFilePath, fileBytes);
                var metaObj = new Dictionary<string, string>
                {
                    ["ContentType"] = contentType,
                    ["FileName"] = fileName,
                    ["Url"] = absoluteUrl,
                    ["DownloadedAt"] = DateTime.UtcNow.ToString("o")
                };
                await File.WriteAllTextAsync(cachedMetadataPath, JsonSerializer.Serialize(metaObj));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed caching downloaded file to disk");
            }

            return (fileBytes, contentType, fileName);
        }

        public async Task<ZambiaDocumentAnalysisResponse> AnalyzeZambiaDocumentAsync(ZambiaDocumentAnalysisRequest request)
        {
            var tenderTitle = request.TenderTitle ?? "Zambia Procurement Document";
            var textToAnalyze = request.DocumentText ?? "";

            if (string.IsNullOrWhiteSpace(textToAnalyze) && !string.IsNullOrWhiteSpace(request.DocumentUrl))
            {
                try
                {
                    var (bytes, contentType, fileName) = await DownloadDocumentFileAsync(request.DocumentUrl);
                    if (contentType.Contains("text") || contentType.Contains("html") || contentType.Contains("json"))
                    {
                        textToAnalyze = Encoding.UTF8.GetString(bytes);
                    }
                    else
                    {
                        textToAnalyze = $"File: {fileName}, Type: {contentType}, Size: {bytes.Length} bytes";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not fetch document content from URL for analysis");
                }
            }

            var apiKey = _configuration["Gemini:ApiKey"];
            var modelName = _configuration["Gemini:ModelName"] ?? "gemini-2.5-flash";

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return GenerateFallbackAnalysis(request, textToAnalyze);
            }

            try
            {
                var prompt = $@"You are a senior public procurement specialist analyzing a Zambia tender or procurement document.
Analyze the following document and output ONLY valid JSON matching this schema:
{{
  ""executiveSummary"": ""brief 2-3 sentence overview of this tender/procurement requirement"",
  ""keyRequirements"": [""list of 3-6 core technical or operational requirements""],
  ""mandatoryEligibility"": [""list of mandatory eligibility criteria, statutory certificates, or compliance prerequisites in Zambia""],
  ""evaluationCriteria"": [""list of evaluation criteria, scoring basis, or financial requirements""],
  ""checklistItems"": [""actionable bid preparation checklist items for bidders""],
  ""identifiedRisks"": [""list of potential compliance or delivery risks""],
  ""recommendedAction"": ""practical recommendation for prospective bidders""
}}

Tender Title: {tenderTitle}
Procuring Entity: {request.ProcuringEntity ?? "Not specified"}
Document Content:
{(textToAnalyze.Length > 12000 ? textToAnalyze[..12000] : textToAnalyze)}
";

                var payload = new
                {
                    contents = new[]
                    {
                        new
                        {
                            parts = new[]
                            {
                                new { text = prompt }
                            }
                        }
                    },
                    generationConfig = new
                    {
                        temperature = 0.2,
                        responseMimeType = "application/json"
                    }
                };

                var requestJson = JsonSerializer.Serialize(payload);
                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelName}:generateContent?key={apiKey}";
                var httpResponse = await _httpClient.PostAsync(url, new StringContent(requestJson, Encoding.UTF8, "application/json"));

                if (httpResponse.IsSuccessStatusCode)
                {
                    var responseBody = await httpResponse.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseBody);
                    var candidates = doc.RootElement.GetProperty("candidates");
                    if (candidates.GetArrayLength() > 0)
                    {
                        var text = candidates[0]
                            .GetProperty("content")
                            .GetProperty("parts")[0]
                            .GetProperty("text")
                            .GetString();

                        if (!string.IsNullOrEmpty(text))
                        {
                            var cleanJson = CleanJsonString(text);
                            var parsed = JsonSerializer.Deserialize<ZambiaDocumentAnalysisResponse>(cleanJson, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });

                            if (parsed != null)
                            {
                                parsed.TenderTitle = tenderTitle;
                                parsed.AnalysisTimestamp = DateTime.UtcNow;
                                return parsed;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gemini AI call failed, falling back to rule-based analysis");
            }

            return GenerateFallbackAnalysis(request, textToAnalyze);
        }

        private static ZambiaDocumentAnalysisResponse GenerateFallbackAnalysis(ZambiaDocumentAnalysisRequest request, string text)
        {
            var title = request.TenderTitle ?? "Zambia Procurement Document";
            return new ZambiaDocumentAnalysisResponse
            {
                TenderTitle = title,
                ExecutiveSummary = $"Procurement opportunity for '{title}' by {request.ProcuringEntity ?? "the procuring authority"} in Zambia.",
                KeyRequirements = new List<string>
                {
                    "Detailed technical and financial proposal submission",
                    "Adherence to specified delivery schedules and scope of works",
                    "Submission of priced bills of quantities / activity schedules"
                },
                MandatoryEligibility = new List<string>
                {
                    "Valid ZPPA Supplier Registration Certificate",
                    "Valid Tax Clearance Certificate (Zambia Revenue Authority)",
                    "Certificate of Incorporation / Business Registration",
                    "Proof of valid NAPSA Social Security compliance"
                },
                EvaluationCriteria = new List<string>
                {
                    "Preliminary / Mandatory compliance check",
                    "Technical capability and past experience verification",
                    "Financial evaluation & price competitiveness"
                },
                ChecklistItems = new List<string>
                {
                    "Verify tender reference number and submission deadline",
                    "Ensure all mandatory statutory documents are certified and up to date",
                    "Prepare bid security / bid securing declaration if required",
                    "Review submission instructions and electronic bidding requirements"
                },
                IdentifiedRisks = new List<string>
                {
                    "Disqualification for missing statutory documentation",
                    "Late submission past the strict electronic cutoff time"
                },
                RecommendedAction = "Complete document verification and submit well in advance of the deadline through the e-GP portal.",
                AnalysisTimestamp = DateTime.UtcNow
            };
        }

        private static string CleanJsonString(string text)
        {
            var match = Regex.Match(text, @"```(?:json)?\s*([\s\S]*?)\s*```");
            return match.Success ? match.Groups[1].Value.Trim() : text.Trim();
        }

        private ZambiaTenderBatch ParseOpenedTendersHtml(string html, int pageNumber, string sourceUrl)
        {
            var batch = new ZambiaTenderBatch
            {
                Page = pageNumber,
                ScrapedAt = DateTime.UtcNow
            };

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var rows = doc.DocumentNode.SelectNodes("//table[@id='T01']//tbody//tr");
            if (rows == null || rows.Count == 0)
            {
                _logger.LogWarning("No tender rows found on Zambia page {Page}", pageNumber);
                return batch;
            }

            foreach (var row in rows)
            {
                var cells = row.SelectNodes("td");
                if (cells == null || cells.Count < 6)
                    continue;

                try
                {
                    var titleAnchor = cells[1].SelectSingleNode(".//a");
                    var rawTitle = titleAnchor != null ? titleAnchor.InnerText : cells[1].InnerText;
                    var title = CleanText(rawTitle);

                    var href = titleAnchor?.GetAttributeValue("href", "") ?? "";
                    var detailsUrl = MakeAbsoluteUrl(href);
                    var resourceId = ExtractResourceId(href);

                    var refNumber = CleanText(cells[2].InnerText);
                    var pe = CleanText(cells[3].InnerText);

                    var deadlineStr = CleanText(cells[4].InnerText);
                    var submissionDeadline = ParseZambiaDate(deadlineStr);

                    var method = CleanText(cells[5].InnerText);

                    var openedBidsAnchor = cells.Count > 6 ? cells[6].SelectSingleNode(".//a") : null;
                    var openedBidsHref = openedBidsAnchor?.GetAttributeValue("href", "") ?? "";
                    var openedBidsUrl = MakeAbsoluteUrl(openedBidsHref);

                    DateTime? awardDate = null;
                    if (cells.Count > 7)
                    {
                        var awardDateStr = CleanText(cells[7].InnerText);
                        if (!string.IsNullOrWhiteSpace(awardDateStr))
                        {
                            awardDate = ParseZambiaDate(awardDateStr);
                        }
                    }

                    var status = cells.Count > 8 ? CleanText(cells[8].InnerText) : "";

                    var tender = new ZambiaTender
                    {
                        Id = !string.IsNullOrEmpty(resourceId) ? resourceId : (!string.IsNullOrEmpty(refNumber) ? refNumber : Guid.NewGuid().ToString("N")),
                        ResourceId = resourceId,
                        Title = title,
                        ReferenceNumber = refNumber,
                        ProcuringEntity = pe,
                        SubmissionDeadline = submissionDeadline,
                        ProcurementMethod = method,
                        OpenedBidsUrl = openedBidsUrl,
                        AwardDate = awardDate,
                        Status = status,
                        DetailsUrl = detailsUrl,
                        PageNumber = pageNumber,
                        SourceUrl = sourceUrl,
                        SourceCountry = "Zambia",
                        ScrapedAt = DateTime.UtcNow
                    };

                    batch.Tenders.Add(tender);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing row on Zambia page {Page}", pageNumber);
                }
            }

            batch.TotalTenders = batch.Tenders.Count;
            return batch;
        }

        private Task<int> GetTotalPagesFromHtmlOrCacheAsync(string html)
        {
            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var lastNav = doc.DocumentNode.SelectSingleNode("//*[@id='lastNav']");
                var href = lastNav?.GetAttributeValue("href", "");
                if (!string.IsNullOrEmpty(href))
                {
                    var match = Regex.Match(href, @"[?&]d-3680181-p=(\d+)");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int total))
                    {
                        _cache.Set(CacheKeyTotalPages, total, TimeSpan.FromHours(1));
                        return Task.FromResult(total);
                    }
                }

                var links = doc.DocumentNode.SelectNodes("//a[contains(@href, 'd-3680181-p=')] | //button[contains(@href, 'd-3680181-p=')]");
                if (links != null)
                {
                    int maxPage = 1;
                    foreach (var link in links)
                    {
                        var linkHref = link.GetAttributeValue("href", "");
                        var match = Regex.Match(linkHref, @"[?&]d-3680181-p=(\d+)");
                        if (match.Success && int.TryParse(match.Groups[1].Value, out int pageNum))
                        {
                            if (pageNum > maxPage) maxPage = pageNum;
                        }
                    }
                    if (maxPage > 1)
                    {
                        _cache.Set(CacheKeyTotalPages, maxPage, TimeSpan.FromHours(1));
                        return Task.FromResult(maxPage);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract total pages from Zambia HTML");
            }

            return Task.FromResult(1);
        }

        private static string CleanText(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var decoded = System.Net.WebUtility.HtmlDecode(input);
            return Regex.Replace(decoded, @"\s+", " ").Trim();
        }

        private static string MakeAbsoluteUrl(string href)
        {
            if (string.IsNullOrWhiteSpace(href)) return string.Empty;
            if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return href;
            }
            return $"{BaseUrl.TrimEnd('/')}/{(href.StartsWith("/") ? href.Substring(1) : href)}";
        }

        private static string ExtractResourceId(string href)
        {
            if (string.IsNullOrWhiteSpace(href)) return string.Empty;
            var match = Regex.Match(href, @"resourceId=(\d+)", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : string.Empty;
        }

        private static DateTime? ParseZambiaDate(string dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr)) return null;

            try
            {
                var cleanDate = Regex.Replace(dateStr, @"\b(CAT|UTC|GMT|EAT|WAT|SAST)\b", "").Trim();
                cleanDate = Regex.Replace(cleanDate, @"\s+", " ");

                string[] formats = new[]
                {
                    "ddd MMM d HH:mm:ss yyyy",
                    "ddd MMM dd HH:mm:ss yyyy",
                    "dd/MM/yyyy HH:mm:ss",
                    "dd/MM/yyyy HH:mm",
                    "dd/MM/yyyy",
                    "yyyy-MM-dd HH:mm:ss",
                    "yyyy-MM-dd"
                };

                if (DateTime.TryParseExact(cleanDate, formats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                {
                    return dt;
                }

                if (DateTime.TryParse(cleanDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var genericDt))
                {
                    return genericDt;
                }
            }
            catch
            {
                // Ignore parse failure
            }

            return null;
        }
    }
}
