using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZimbabweTenderAPI.Models;

namespace ZimbabweTenderAPI.Services
{
    public class MultiSourceProcurementService : IMultiSourceProcurementService
    {
        private readonly HttpClient _httpClient;
        private readonly IMemoryCache _cache;
        private readonly ILogger<MultiSourceProcurementService> _logger;
        private readonly IZambiaTenderScraperService? _zambiaScraperService;
        private readonly IServiceProvider _serviceProvider;

        public MultiSourceProcurementService(
            HttpClient httpClient,
            IMemoryCache cache,
            ILogger<MultiSourceProcurementService> logger,
            IServiceProvider serviceProvider)
        {
            _httpClient = httpClient;
            _cache = cache;
            _logger = logger;
            _serviceProvider = serviceProvider;
            _zambiaScraperService = (IZambiaTenderScraperService?)serviceProvider.GetService(typeof(IZambiaTenderScraperService));

            // Set standard user agent
            if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            {
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            }
        }

        #region 1. GoZambiaJobs Tenders & RFPs

        public async Task<List<MultiSourceTenderDto>> GetGoZambiaJobsTendersAsync(int page = 1, string? keyword = null)
        {
            var cacheKey = $"multisource_gozambia_{page}_{keyword?.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<MultiSourceTenderDto>? cached) && cached != null)
            {
                return cached;
            }

            var results = new List<MultiSourceTenderDto>();
            try
            {
                var rssUrl = "https://gozambiajobs.com/rss/jobs?filters%5B35172%5D=57723&order=relevance";
                _logger.LogInformation("Fetching GoZambiaJobs tenders RSS from {Url}", rssUrl);

                var response = await _httpClient.GetAsync(rssUrl);
                if (response.IsSuccessStatusCode)
                {
                    var xmlContent = await response.Content.ReadAsStringAsync();
                    var xdoc = XDocument.Parse(xmlContent);
                    var items = xdoc.Descendants("item");

                    foreach (var item in items)
                    {
                        var title = item.Element("title")?.Value ?? "";
                        var company = item.Element("company")?.Value ?? "";
                        var descHtml = item.Element("description")?.Value ?? "";
                        var link = item.Element("link")?.Value ?? "";
                        var guid = item.Element("guid")?.Value ?? "";
                        var location = item.Element("location")?.Value ?? "Zambia";
                        var pubDateStr = item.Element("pubDate")?.Value ?? "";

                        var cleanDesc = CleanHtml(descHtml);
                        var docLinks = ExtractLinks(descHtml);

                        DateTime? pubDate = null;
                        if (DateTime.TryParse(pubDateStr, out var pd)) pubDate = pd;

                        var dto = new MultiSourceTenderDto
                        {
                            SourcePortal = "GoZambiaJobs",
                            TenderId = !string.IsNullOrEmpty(guid) ? guid : Guid.NewGuid().ToString("N").Substring(0, 8),
                            Title = title,
                            ProcuringEntity = !string.IsNullOrEmpty(company) ? company : "GoZambiaJobs Issuer",
                            Country = "Zambia",
                            Location = location,
                            ProcurementType = title.ToLowerInvariant().Contains("rfp") || cleanDesc.ToLowerInvariant().Contains("request for proposal") ? "RFP" : "Tender",
                            PublishDate = pubDate,
                            SourceUrl = link,
                            DocumentUrls = docLinks,
                            PdfUrl = docLinks.FirstOrDefault(l => l.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) || l.Contains("drive.google.com")),
                            Description = cleanDesc,
                            ScrapedAt = DateTime.UtcNow
                        };

                        if (MatchesKeyword(dto, keyword))
                        {
                            results.Add(dto);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching GoZambiaJobs tenders");
            }

            _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
            return results;
        }

        #endregion

        #region 2. OnlineTenders Zambia

        public async Task<List<MultiSourceTenderDto>> GetOnlineTendersZambiaAsync(int page = 1, string? keyword = null)
        {
            var cacheKey = $"multisource_onlinetenders_{page}_{keyword?.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<MultiSourceTenderDto>? cached) && cached != null)
            {
                return cached;
            }

            var results = new List<MultiSourceTenderDto>();
            try
            {
                var url = page <= 1 ? "https://www.onlinetenders.co.za/tenders/zambia" : $"https://www.onlinetenders.co.za/tenders/zambia?page={page}";
                _logger.LogInformation("Fetching OnlineTenders Zambia from {Url}", url);

                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var tenderNodes = doc.DocumentNode.SelectNodes("//a[contains(@href, 'javascript:void(0)') or contains(@href, '/tenders/')]");
                    if (tenderNodes != null)
                    {
                        int idx = 1;
                        foreach (var node in tenderNodes)
                        {
                            var title = CleanHtml(node.InnerText);
                            if (string.IsNullOrWhiteSpace(title) || title.Length < 10 || title.Equals("VIEW TENDERS", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var dto = new MultiSourceTenderDto
                            {
                                SourcePortal = "OnlineTenders",
                                TenderId = $"OT-ZM-{page}-{idx}",
                                Title = title,
                                ProcuringEntity = "OnlineTenders Listed Entity",
                                Country = "Zambia",
                                Location = "Zambia",
                                ProcurementType = title.Contains("RFP", StringComparison.OrdinalIgnoreCase) ? "RFP" : "Tender",
                                SourceUrl = "https://www.onlinetenders.co.za/tenders/zambia",
                                Description = title,
                                ScrapedAt = DateTime.UtcNow
                            };

                            if (MatchesKeyword(dto, keyword))
                            {
                                results.Add(dto);
                                idx++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching OnlineTenders Zambia");
            }

            _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
            return results;
        }

        #endregion

        #region 2b. OnlineTenders Zimbabwe

        public async Task<List<MultiSourceTenderDto>> GetOnlineTendersZimbabweAsync(int page = 1, string? keyword = null)
        {
            var cacheKey = $"multisource_onlinetenders_zw_{page}_{keyword?.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<MultiSourceTenderDto>? cached) && cached != null)
            {
                return cached;
            }

            var results = new List<MultiSourceTenderDto>();
            try
            {
                var url = page <= 1 ? "https://www.onlinetenders.co.za/tenders/zimbabwe" : $"https://www.onlinetenders.co.za/tenders/zimbabwe?page={page}";
                _logger.LogInformation("Fetching OnlineTenders Zimbabwe from {Url}", url);

                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var tenderNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'tender') and @data-tid]");
                    if (tenderNodes != null)
                    {
                        foreach (var node in tenderNodes)
                        {
                            var tid = node.GetAttributeValue("data-tid", "");
                            var cnNode = node.SelectSingleNode(".//div[contains(@class, 'tender-cn')]");
                            var descNode = node.SelectSingleNode(".//div[contains(@class, 'tender-desc')]");
                            var cdNode = node.SelectSingleNode(".//div[contains(@class, 'tender-cd')]");

                            var rawRefNo = CleanContractNumber(cnNode?.InnerText);
                            var desc = CleanHtml(descNode?.InnerText ?? "").Replace("show more details...", "").Trim();
                            var closingStr = CleanHtml(cdNode?.InnerText ?? "").Trim();

                            if (string.IsNullOrWhiteSpace(desc) || desc.Length < 10) continue;

                            var refNo = !string.IsNullOrWhiteSpace(rawRefNo) ? rawRefNo : $"OT-ZW-{tid}";
                            var title = CleanTenderTitle(desc);
                            var entity = InferProcuringEntity(refNo, desc);

                            DateTime? closingDate = ParseFlexibleDate(closingStr);

                            var dto = new MultiSourceTenderDto
                            {
                                SourcePortal = "OnlineTenders",
                                TenderId = refNo,
                                Title = title,
                                ProcuringEntity = entity,
                                Country = "Zimbabwe",
                                Location = "Harare, Zimbabwe",
                                ProcurementType = refNo.Contains("EOI", StringComparison.OrdinalIgnoreCase) || desc.Contains("Expression of Interest", StringComparison.OrdinalIgnoreCase) ? "EOI" : "Tender",
                                SourceUrl = "https://www.onlinetenders.co.za/tenders/zimbabwe",
                                Description = desc,
                                ClosingDate = closingDate,
                                ScrapedAt = DateTime.UtcNow
                            };

                            if (MatchesKeyword(dto, keyword))
                            {
                                results.Add(dto);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching OnlineTenders Zimbabwe");
            }

            _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
            return results;
        }

        #endregion

        #region 3. World Bank Procurement Notices

        public async Task<List<MultiSourceTenderDto>> GetWorldBankTendersAsync(string countryCode = "ZM", int page = 1, int pageSize = 20, string? keyword = null)
        {
            var cacheKey = $"multisource_worldbank_{countryCode}_{page}_{pageSize}_{keyword?.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<MultiSourceTenderDto>? cached) && cached != null)
            {
                return cached;
            }

            var results = new List<MultiSourceTenderDto>();
            try
            {
                var offset = (page - 1) * pageSize;
                var countryTerm = countryCode.Equals("ZM", StringComparison.OrdinalIgnoreCase) ? "Zambia" :
                                 (countryCode.Equals("ZW", StringComparison.OrdinalIgnoreCase) || countryCode.Equals("all", StringComparison.OrdinalIgnoreCase) ? "Zimbabwe" :
                                 countryCode.Equals("MW", StringComparison.OrdinalIgnoreCase) ? "Malawi" :
                                 countryCode.Equals("TZ", StringComparison.OrdinalIgnoreCase) ? "Tanzania" : countryCode);
                var url = $"https://search.worldbank.org/api/v2/procnotices?format=json&rows={pageSize}&os={offset}";
                if (!string.IsNullOrWhiteSpace(countryTerm))
                {
                    url += $"&project_ctry_name_exact={Uri.EscapeDataString(countryTerm)}";
                }
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    url += $"&qterm={Uri.EscapeDataString(keyword)}";
                }

                _logger.LogInformation("Fetching World Bank procurement notices from {Url}", url);

                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var jsonStr = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonStr);

                    if (doc.RootElement.TryGetProperty("procnotices", out var noticesElem))
                    {
                        var noticeElements = new List<JsonElement>();
                        if (noticesElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in noticesElem.EnumerateArray()) noticeElements.Add(item);
                        }
                        else if (noticesElem.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in noticesElem.EnumerateObject()) noticeElements.Add(prop.Value);
                        }

                        foreach (var elem in noticeElements)
                        {
                            var id = elem.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                            var noticeType = elem.TryGetProperty("notice_type", out var ntProp) ? ntProp.GetString() ?? "Tender" : "Tender";
                            var noticeDateStr = elem.TryGetProperty("noticedate", out var ndProp) ? ndProp.GetString() ?? "" : "";
                            var deadlineStr = elem.TryGetProperty("submission_deadline_date", out var dlProp) ? dlProp.GetString() ?? "" : "";
                            var ctry = elem.TryGetProperty("project_ctry_name", out var ctryProp) ? ctryProp.GetString() ?? "Zimbabwe" : "Zimbabwe";
                            var projectName = elem.TryGetProperty("project_name", out var pnProp) ? pnProp.GetString() ?? "" : "";
                            var bidRef = elem.TryGetProperty("bid_reference_no", out var brProp) ? brProp.GetString() ?? "" : "";
                            var bidDesc = elem.TryGetProperty("bid_description", out var bdProp) ? bdProp.GetString() ?? "" : "";
                            var contactOrg = elem.TryGetProperty("contact_organization", out var coProp) ? coProp.GetString() ?? "" : "";
                            var contactEmail = elem.TryGetProperty("contact_email", out var ceProp) ? ceProp.GetString() ?? "" : "";
                            var contactName = elem.TryGetProperty("contact_name", out var cnProp) ? cnProp.GetString() ?? "" : "";
                            var procMethod = elem.TryGetProperty("procurement_method_name", out var pmProp) ? pmProp.GetString() ?? "" : "";
                            var noticeText = elem.TryGetProperty("notice_text", out var ntxProp) ? ntxProp.GetString() ?? "" : "";

                            DateTime? publishDate = ParseFlexibleDate(noticeDateStr);
                            DateTime? closingDate = ParseFlexibleDate(deadlineStr);

                            var title = !string.IsNullOrEmpty(bidDesc) ? bidDesc : (!string.IsNullOrEmpty(projectName) ? $"{projectName} - {noticeType}" : id);

                            var dto = new MultiSourceTenderDto
                            {
                                SourcePortal = "WorldBank",
                                TenderId = !string.IsNullOrEmpty(id) ? id : bidRef,
                                Title = title,
                                ProcuringEntity = !string.IsNullOrEmpty(contactOrg) ? contactOrg : (!string.IsNullOrEmpty(projectName) ? $"{projectName} Implementing Entity" : "Government of Zimbabwe"),
                                Country = ctry,
                                Location = ctry,
                                ProcurementType = noticeType,
                                CommodityGroup = procMethod,
                                PublishDate = publishDate,
                                ClosingDate = closingDate,
                                SourceUrl = $"https://projects.worldbank.org/en/projects-operations/procurement-detail/{id}",
                                Description = CleanHtml(noticeText),
                                ContactEmail = contactEmail,
                                ContactName = contactName,
                                ScrapedAt = DateTime.UtcNow
                            };

                            if (MatchesKeyword(dto, keyword))
                            {
                                results.Add(dto);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching World Bank procurement notices");
            }

            _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
            return results;
        }

        #endregion

        #region 4. UN Global Procurement (Tenders & EOIs)

        public async Task<List<MultiSourceTenderDto>> GetUnProcurementNoticesAsync(string noticeType = "all", string? keyword = null)
        {
            var cacheKey = $"multisource_un_{noticeType}_{keyword?.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<MultiSourceTenderDto>? cached) && cached != null)
            {
                return cached;
            }

            var results = new List<MultiSourceTenderDto>();

            // 1. Tenders CSV
            if (noticeType.Equals("all", StringComparison.OrdinalIgnoreCase) || noticeType.Equals("tender", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var tenderCsvUrl = "https://www.un.org/procurement/tender.csv/all/all";
                    _logger.LogInformation("Fetching UN tenders CSV from {Url}", tenderCsvUrl);

                    var response = await _httpClient.GetAsync(tenderCsvUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var csv = await response.Content.ReadAsStringAsync();
                        using var reader = new StringReader(csv);
                        string? line;
                        bool headerSkipped = false;

                        while ((line = reader.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            if (!headerSkipped) { headerSkipped = true; continue; }

                            var fields = ParseCsvLine(line);
                            if (fields.Count >= 2)
                            {
                                var bidNo = fields[0].Trim('\"', ' ');
                                var title = fields[1].Trim('\"', ' ');
                                var openDateStr = fields.Count > 2 ? fields[2].Trim('\"', ' ') : "";
                                var openTime = fields.Count > 3 ? fields[3].Trim('\"', ' ') : "";
                                var commodity = fields.Count > 4 ? fields[4].Trim('\"', ' ') : "";

                                var dto = new MultiSourceTenderDto
                                {
                                    SourcePortal = "UNProcurement",
                                    TenderId = bidNo,
                                    Title = title,
                                    ProcuringEntity = "United Nations Procurement Division (UNPD)",
                                    Country = "Global / International",
                                    Location = "UN Worldwide Operations",
                                    ProcurementType = "Tender",
                                    CommodityGroup = commodity,
                                    PublishDate = ParseFlexibleDate(openDateStr),
                                    SourceUrl = "https://www.un.org/procurement/solicitations-opportunities#tender",
                                    Description = $"UN Solicitations Tender - Bid No: {bidNo}. Commodity: {commodity}. Opening: {openDateStr} {openTime}",
                                    ScrapedAt = DateTime.UtcNow
                                };

                                if (MatchesKeyword(dto, keyword))
                                {
                                    results.Add(dto);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching UN tenders CSV");
                }
            }

            // 2. Expressions of Interest (EOI) CSV
            if (noticeType.Equals("all", StringComparison.OrdinalIgnoreCase) || noticeType.Equals("eoi", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var eoiCsvUrl = "https://www.un.org/procurement/eoi.csv";
                    _logger.LogInformation("Fetching UN EOI CSV from {Url}", eoiCsvUrl);

                    var response = await _httpClient.GetAsync(eoiCsvUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        var csv = await response.Content.ReadAsStringAsync();
                        using var reader = new StringReader(csv);
                        string? line;
                        bool headerSkipped = false;

                        while ((line = reader.ReadLine()) != null)
                        {
                            if (string.IsNullOrWhiteSpace(line)) continue;
                            if (!headerSkipped) { headerSkipped = true; continue; }

                            var fields = ParseCsvLine(line);
                            if (fields.Count >= 2)
                            {
                                var eoiNo = fields[0].Trim('\"', ' ');
                                var title = fields[1].Trim('\"', ' ');
                                var commodity = fields.Count > 2 ? fields[2].Trim('\"', ' ') : "";
                                var startDateStr = fields.Count > 3 ? fields[3].Trim('\"', ' ') : "";
                                var expiryDateStr = fields.Count > 4 ? fields[4].Trim('\"', ' ') : "";
                                var pdfUrl = fields.Count > 5 ? fields[5].Trim('\"', ' ') : "";

                                var dto = new MultiSourceTenderDto
                                {
                                    SourcePortal = "UNProcurement",
                                    TenderId = eoiNo,
                                    Title = title,
                                    ProcuringEntity = "United Nations Global Procurement",
                                    Country = "Global / International",
                                    Location = "UN Global",
                                    ProcurementType = "EOI",
                                    CommodityGroup = commodity,
                                    PublishDate = ParseFlexibleDate(startDateStr),
                                    ClosingDate = ParseFlexibleDate(expiryDateStr),
                                    SourceUrl = "https://www.un.org/procurement/solicitations-opportunities#eoi",
                                    PdfUrl = pdfUrl,
                                    DocumentUrls = !string.IsNullOrEmpty(pdfUrl) ? new List<string> { pdfUrl } : new List<string>(),
                                    Description = $"UN Expression of Interest (EOI No: {eoiNo}). Commodity: {commodity}. Start: {startDateStr}, Expiry: {expiryDateStr}",
                                    ScrapedAt = DateTime.UtcNow
                                };

                                if (MatchesKeyword(dto, keyword))
                                {
                                    results.Add(dto);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error fetching UN EOI CSV");
                }
            }

            _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
            return results;
        }

        #endregion

        #region 4b. UNGM / UN Zimbabwe & Zambia Procurement Notices

        public async Task<List<MultiSourceTenderDto>> GetUngmCountryTendersAsync(string countryCode = "ZW", int page = 1, string? keyword = null)
        {
            if (countryCode.Equals("ZM", StringComparison.OrdinalIgnoreCase))
            {
                return await GetUngmZambiaTendersAsync(keyword);
            }
            return await GetUngmZimbabweTendersAsync(keyword);
        }

        public async Task<List<MultiSourceTenderDto>> GetUngmZambiaTendersAsync(string? keyword = null)
        {
            var cacheKey = $"multisource_ungm_zm_{keyword?.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<MultiSourceTenderDto>? cached) && cached != null)
            {
                return cached;
            }

            var results = new List<MultiSourceTenderDto>();
            try
            {
                var allUnTenders = await GetUnProcurementNoticesAsync("all", keyword);
                foreach (var t in allUnTenders)
                {
                    if (t.Title.Contains("Zambia", StringComparison.OrdinalIgnoreCase) ||
                        t.Description?.Contains("Zambia", StringComparison.OrdinalIgnoreCase) == true ||
                        t.Location.Contains("Zambia", StringComparison.OrdinalIgnoreCase))
                    {
                        t.Country = "Zambia";
                        t.Location = "Lusaka, Zambia";
                        t.SourcePortal = "UNGM";
                        results.Add(t);
                    }
                }

                // Live notices are scraped directly from UNGM via scripts/scrape_ungm.py
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching UNGM Zambia tenders");
            }

            _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
            return results;
        }

        public async Task<List<MultiSourceTenderDto>> GetUngmZimbabweTendersAsync(string? keyword = null)
        {
            var cacheKey = $"multisource_ungm_zw_{keyword?.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<MultiSourceTenderDto>? cached) && cached != null)
            {
                return cached;
            }

            var results = new List<MultiSourceTenderDto>();
            try
            {
                var allUnTenders = await GetUnProcurementNoticesAsync("all", keyword);
                foreach (var t in allUnTenders)
                {
                    if (t.Title.Contains("Zimbabwe", StringComparison.OrdinalIgnoreCase) ||
                        t.Description?.Contains("Zimbabwe", StringComparison.OrdinalIgnoreCase) == true ||
                        t.Location.Contains("Zimbabwe", StringComparison.OrdinalIgnoreCase))
                    {
                        t.Country = "Zimbabwe";
                        t.Location = "Harare, Zimbabwe";
                        t.SourcePortal = "UNGM";
                        results.Add(t);
                    }
                }

                // Live notices are scraped directly from UNGM via scripts/scrape_ungm.py
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching UNGM Zimbabwe tenders");
            }

            _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
            return results;
        }

        #endregion

        #region 4c. African Development Bank (AfDB) Zimbabwe Notices

        public async Task<List<MultiSourceTenderDto>> GetAfdbZimbabweTendersAsync(string? keyword = null)
        {
            var cacheKey = $"multisource_afdb_zw_{keyword?.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<MultiSourceTenderDto>? cached) && cached != null)
            {
                return cached;
            }

            var results = new List<MultiSourceTenderDto>();
            try
            {
                var config = _serviceProvider.GetService<IConfiguration>();
                var supabaseUrl = config?["Supabase:Url"] ?? "https://pqqymbdbkwltzydymild.supabase.co";
                var supabaseKey = config?["Supabase:SecretKey"] ?? config?["Supabase:PublishableKey"];

                if (string.IsNullOrEmpty(supabaseKey))
                {
                    var envPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "next-shadcn-admin-dashboard", ".env.local");
                    if (File.Exists(envPath))
                    {
                        foreach (var line in File.ReadAllLines(envPath))
                        {
                            if (line.StartsWith("SUPABASE_SECRET_KEY="))
                                supabaseKey = line.Substring("SUPABASE_SECRET_KEY=".Length).Trim().Trim('"');
                        }
                    }
                }

                if (!string.IsNullOrEmpty(supabaseKey))
                {
                    using var req = new HttpRequestMessage(HttpMethod.Get, $"{supabaseUrl.TrimEnd('/')}/rest/v1/tenders?source=eq.afdb&status=eq.live&select=payload&limit=250");
                    req.Headers.Add("apikey", supabaseKey);
                    req.Headers.Add("Authorization", $"Bearer {supabaseKey}");

                    var res = await _httpClient.SendAsync(req);
                    if (res.IsSuccessStatusCode)
                    {
                        var json = await res.Content.ReadAsStringAsync();
                        using var doc = JsonDocument.Parse(json);
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            if (elem.TryGetProperty("payload", out var payload))
                            {
                                var title = payload.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                                if (!string.IsNullOrEmpty(keyword) && !title.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                                    continue;

                                var tid = payload.TryGetProperty("tenderId", out var ti) ? ti.GetString() ?? "" : "";
                                var desc = payload.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                                var sourceUrl = payload.TryGetProperty("sourceUrl", out var su) ? su.GetString() ?? "" : "";
                                var pEntity = payload.TryGetProperty("procuringEntity", out var pe) ? pe.GetString() ?? "" : "African Development Bank (AfDB)";
                                var pType = payload.TryGetProperty("procurementType", out var pt) ? pt.GetString() ?? "" : "EOI - Expression of Interest";
                                var cGroup = payload.TryGetProperty("commodityGroup", out var cg) ? cg.GetString() ?? "" : "General Procurement";

                                DateTime? pubDate = null;
                                if (payload.TryGetProperty("publishDate", out var pd) && DateTime.TryParse(pd.GetString(), out var parsedPd))
                                    pubDate = parsedPd;

                                DateTime? closeDate = null;
                                if (payload.TryGetProperty("closingDate", out var cd) && DateTime.TryParse(cd.GetString(), out var parsedCd))
                                    closeDate = parsedCd;

                                results.Add(new MultiSourceTenderDto
                                {
                                    SourcePortal = "AfDB",
                                    TenderId = tid,
                                    Title = title,
                                    ProcuringEntity = pEntity,
                                    Country = "Zimbabwe",
                                    Location = "Zimbabwe",
                                    ProcurementType = pType,
                                    CommodityGroup = cGroup,
                                    PublishDate = pubDate,
                                    ClosingDate = closeDate,
                                    SourceUrl = sourceUrl,
                                    Description = desc,
                                    ScrapedAt = DateTime.UtcNow
                                });
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching AfDB Zimbabwe procurement notices");
            }

            _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
            return results;
        }

        #endregion

        #region 5. EU Funding & Tenders Portal

        public async Task<List<MultiSourceTenderDto>> GetEuFundingTendersAsync(int pageNumber = 1, int pageSize = 20, string? keyword = null)
        {
            var cacheKey = $"multisource_eufunding_{pageNumber}_{pageSize}_{keyword?.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<MultiSourceTenderDto>? cached) && cached != null)
            {
                return cached;
            }

            var results = new List<MultiSourceTenderDto>();
            try
            {
                var queryText = !string.IsNullOrWhiteSpace(keyword) ? keyword : "**";
                var url = $"https://api.tech.ec.europa.eu/search-api/prod/rest/search?apiKey=SEDIA&text={Uri.EscapeDataString(queryText)}";
                _logger.LogInformation("Fetching EU Funding and Tenders from {Url}", url);

                var payload = new
                {
                    query = new
                    {
                        boolQuery = new
                        {
                            must = new object[]
                            {
                                new { terms = new { type = new[] { "0", "1", "2", "8" } } },
                                new { terms = new { status = new[] { "31094501", "31094502" } } }
                            }
                        }
                    },
                    pageNumber = pageNumber,
                    pageSize = pageSize
                };

                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonStr = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonStr);

                    if (doc.RootElement.TryGetProperty("results", out var resultsElem))
                    {
                        int idx = 1;
                        foreach (var item in resultsElem.EnumerateArray())
                        {
                            var meta = item.TryGetProperty("metadata", out var m) ? m : default;
                            var title = GetStringOrFirstArray(meta, "title");
                            var desc = GetStringOrFirstArray(meta, "description");
                            var itemUrl = GetStringOrFirstArray(meta, "url");
                            var startDateStr = GetStringOrFirstArray(meta, "startDate");
                            var deadlineStr = GetStringOrFirstArray(meta, "deadlineDate");
                            var identifier = GetStringOrFirstArray(meta, "identifier");
                            var prog = GetStringOrFirstArray(meta, "esST_programmes");

                            if (string.IsNullOrEmpty(title)) continue;

                            var dto = new MultiSourceTenderDto
                            {
                                SourcePortal = "EUFunding",
                                TenderId = !string.IsNullOrEmpty(identifier) ? identifier : $"EU-{pageNumber}-{idx}",
                                Title = title,
                                ProcuringEntity = "European Commission",
                                Country = "European Union / International",
                                Location = "EU International Aid & Development",
                                ProcurementType = "Call for Tenders",
                                CommodityGroup = prog,
                                PublishDate = ParseFlexibleDate(startDateStr),
                                ClosingDate = ParseFlexibleDate(deadlineStr),
                                SourceUrl = !string.IsNullOrEmpty(itemUrl) ? itemUrl : "https://ec.europa.eu/info/funding-tenders/opportunities/portal/screen/opportunities/calls-for-tenders",
                                Description = desc,
                                ScrapedAt = DateTime.UtcNow
                            };

                            results.Add(dto);
                            idx++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching EU Funding and Tenders");
            }

            _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
            return results;
        }

        #endregion

        #region 6. DevelopmentAid Tenders & Grants

        public async Task<List<MultiSourceTenderDto>> GetDevelopmentAidTendersAsync(string type = "tenders", int page = 1, string? keyword = null)
        {
            var cacheKey = $"multisource_dev_aid_{type}_{page}_{keyword?.ToLowerInvariant()}";
            if (_cache.TryGetValue(cacheKey, out List<MultiSourceTenderDto>? cached) && cached != null)
            {
                return cached;
            }

            var results = new List<MultiSourceTenderDto>();
            try
            {
                var searchUrl = type.Equals("grants", StringComparison.OrdinalIgnoreCase)
                    ? "https://www.developmentaid.org/grants/search"
                    : "https://www.developmentaid.org/tenders/search";

                _logger.LogInformation("Fetching DevelopmentAid {Type} from {Url}", type, searchUrl);

                var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                request.Headers.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");

                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    var html = await response.Content.ReadAsStringAsync();
                    var doc = new HtmlDocument();
                    doc.LoadHtml(html);

                    var cardNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'tender-card') or contains(@class, 'search-result')]");
                    if (cardNodes != null)
                    {
                        int idx = 1;
                        foreach (var card in cardNodes)
                        {
                            var titleNode = card.SelectSingleNode(".//a[contains(@href, 'tenders') or contains(@href, 'grants')]");
                            var title = CleanHtml(titleNode?.InnerText);
                            var href = titleNode?.GetAttributeValue("href", "") ?? "";

                            if (string.IsNullOrEmpty(title)) continue;

                            var dto = new MultiSourceTenderDto
                            {
                                SourcePortal = "DevelopmentAid",
                                TenderId = $"DA-{type}-{idx}",
                                Title = title,
                                ProcuringEntity = "DevelopmentAid Partner Agency",
                                Country = "International / Africa",
                                ProcurementType = type.Equals("grants", StringComparison.OrdinalIgnoreCase) ? "Grant" : "Tender",
                                SourceUrl = href.StartsWith("http") ? href : $"https://www.developmentaid.org{href}",
                                Description = title,
                                ScrapedAt = DateTime.UtcNow
                            };

                            if (MatchesKeyword(dto, keyword))
                            {
                                results.Add(dto);
                                idx++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "DevelopmentAid direct scraper encounter: {Message}", ex.Message);
            }

            _cache.Set(cacheKey, results, TimeSpan.FromMinutes(15));
            return results;
        }

        #endregion

        #region 7. Unified Multi-Source Aggregator Feed

        public async Task<List<MultiSourceTenderDto>> GetUnifiedProcurementFeedAsync(string? keyword = null, string? country = null, string? portal = null, int limit = 50)
        {
            var combined = new List<MultiSourceTenderDto>();

            var tasks = new List<Task<List<MultiSourceTenderDto>>>();

            bool allPortals = string.IsNullOrWhiteSpace(portal) || portal.Equals("all", StringComparison.OrdinalIgnoreCase);
            bool isZimbabwe = country != null && (country.Equals("Zimbabwe", StringComparison.OrdinalIgnoreCase) || country.Equals("ZW", StringComparison.OrdinalIgnoreCase));
            bool isZambia = country != null && (country.Equals("Zambia", StringComparison.OrdinalIgnoreCase) || country.Equals("ZM", StringComparison.OrdinalIgnoreCase));
            bool allCountries = string.IsNullOrWhiteSpace(country) || country.Equals("all", StringComparison.OrdinalIgnoreCase);

            if ((allPortals || portal!.Equals("GoZambiaJobs", StringComparison.OrdinalIgnoreCase)) && (allCountries || isZambia))
                tasks.Add(GetGoZambiaJobsTendersAsync(1, keyword));

            // Only fetch OnlineTenders if explicitly requested (aggregator notices without documents are excluded from main feed)
            if (portal != null && portal.Equals("OnlineTenders", StringComparison.OrdinalIgnoreCase))
            {
                if (allCountries || isZambia)
                    tasks.Add(GetOnlineTendersZambiaAsync(1, keyword));
                if (allCountries || isZimbabwe)
                    tasks.Add(GetOnlineTendersZimbabweAsync(1, keyword));
            }

            if (allPortals || portal!.Equals("WorldBank", StringComparison.OrdinalIgnoreCase))
            {
                var wbCountry = isZambia ? "ZM" : "ZW";
                tasks.Add(GetWorldBankTendersAsync(wbCountry, 1, 25, keyword));
            }

            if (allPortals || portal!.Equals("UNProcurement", StringComparison.OrdinalIgnoreCase) || portal!.Equals("UNGM", StringComparison.OrdinalIgnoreCase))
            {
                tasks.Add(GetUnProcurementNoticesAsync("all", keyword));
                if (allCountries || isZimbabwe)
                    tasks.Add(GetUngmZimbabweTendersAsync(keyword));
            }

            if ((allPortals || portal!.Equals("AfDB", StringComparison.OrdinalIgnoreCase)) && (allCountries || isZimbabwe))
            {
                tasks.Add(GetAfdbZimbabweTendersAsync(keyword));
            }

            if (portal != null && portal.Equals("EUFunding", StringComparison.OrdinalIgnoreCase))
                tasks.Add(GetEuFundingTendersAsync(1, 20, keyword));

            if (allPortals || portal!.Equals("DevelopmentAid", StringComparison.OrdinalIgnoreCase))
                tasks.Add(GetDevelopmentAidTendersAsync("tenders", 1, keyword));

            // Also integrate ZPPA e-GP live tenders if available
            if ((allPortals || portal!.Equals("ZPPA", StringComparison.OrdinalIgnoreCase)) && (allCountries || isZambia) && _zambiaScraperService != null)
            {
                tasks.Add(GetZppaMappedTendersAsync(keyword));
            }

            var allResults = await Task.WhenAll(tasks);
            foreach (var r in allResults)
            {
                combined.AddRange(r);
            }

            // Filter by country if specified
            if (!string.IsNullOrWhiteSpace(country) && !country.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                combined = combined.Where(t =>
                    t.Country.Contains(country, StringComparison.OrdinalIgnoreCase) ||
                    t.Location.Contains(country, StringComparison.OrdinalIgnoreCase) ||
                    t.Country.Equals("Global / International", StringComparison.OrdinalIgnoreCase)
                ).ToList();
            }

            // Deduplicate by title similarity or ID
            var deduplicated = combined
                .GroupBy(t => t.Title.Trim().ToLowerInvariant())
                .Select(g => g.First())
                .OrderByDescending(t => t.PublishDate ?? DateTime.MinValue)
                .Take(limit)
                .ToList();

            return deduplicated;
        }

        private async Task<List<MultiSourceTenderDto>> GetZppaMappedTendersAsync(string? keyword)
        {
            var list = new List<MultiSourceTenderDto>();
            try
            {
                if (_zambiaScraperService == null) return list;

                var batch = string.IsNullOrWhiteSpace(keyword)
                    ? await _zambiaScraperService.ScrapeOpenedTendersPageAsync(1)
                    : await _zambiaScraperService.SearchOpenedTendersAsync(keyword, 1, 3);

                if (batch?.Tenders != null)
                {
                    foreach (var z in batch.Tenders)
                    {
                        var tid = !string.IsNullOrEmpty(z.ReferenceNumber) ? z.ReferenceNumber : (!string.IsNullOrEmpty(z.ResourceId) ? z.ResourceId : z.Id);
                        list.Add(new MultiSourceTenderDto
                        {
                            SourcePortal = "ZPPA",
                            TenderId = tid,
                            Title = z.Title,
                            ProcuringEntity = z.ProcuringEntity,
                            Country = "Zambia",
                            Location = "Zambia",
                            ProcurementType = "Tender",
                            PublishDate = z.AwardDate,
                            ClosingDate = z.SubmissionDeadline,
                            SourceUrl = !string.IsNullOrEmpty(z.DetailsUrl) ? z.DetailsUrl : z.SourceUrl,
                            Description = z.Title,
                            ScrapedAt = DateTime.UtcNow
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error aggregating ZPPA tenders");
            }
            return list;
        }

        #endregion

        #region Helper Methods

        private static bool MatchesKeyword(MultiSourceTenderDto dto, string? keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;
            var kw = keyword.Trim().ToLowerInvariant();
            return (dto.Title != null && dto.Title.ToLowerInvariant().Contains(kw)) ||
                   (dto.ProcuringEntity != null && dto.ProcuringEntity.ToLowerInvariant().Contains(kw)) ||
                   (dto.Description != null && dto.Description.ToLowerInvariant().Contains(kw)) ||
                   (dto.CommodityGroup != null && dto.CommodityGroup.ToLowerInvariant().Contains(kw));
        }

        private static string CleanContractNumber(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var clean = CleanHtml(raw);
            clean = Regex.Replace(clean, @"\b(New|\d+-\d+\s*days?\s*old)\b", "", RegexOptions.IgnoreCase);
            return Regex.Replace(clean, @"\s+", " ").Trim();
        }

        private static string InferProcuringEntity(string refNo, string desc)
        {
            var lowerRef = refNo.ToLowerInvariant();
            var lowerDesc = desc.ToLowerInvariant();

            if (lowerRef.StartsWith("npa") || lowerDesc.Contains("prosecuting authority"))
                return "National Prosecuting Authority (NPA)";
            if (lowerRef.Contains("zimdef") || lowerDesc.Contains("zimdef") || lowerDesc.Contains("manpower development"))
                return "Zimbabwe Manpower Development Fund (ZIMDEF)";
            if (lowerRef.Contains("cob/") || lowerDesc.Contains("bulawayo") || lowerDesc.Contains("thorngrove"))
                return "City of Bulawayo";
            if (lowerRef.Contains("mrdc/") || lowerDesc.Contains("marondera"))
                return "Marondera Rural District Council (MRDC)";
            if (lowerRef.Contains("7000008750") || lowerDesc.Contains("distribution transformers in eac") || lowerDesc.Contains("meps"))
                return "Regional Centre for Renewable Energy (EACREEE / SACREEE)";
            if (lowerRef.StartsWith("mohcc") || lowerRef.StartsWith("moh/") || lowerDesc.Contains("ministry of health"))
                return "Ministry of Health & Child Care (MOHCC)";
            if (lowerRef.StartsWith("psc") || lowerDesc.Contains("public service commission"))
                return "Public Service Commission (PSC)";
            if (lowerRef.StartsWith("cog") || lowerDesc.Contains("gweru"))
                return "City of Gweru";
            if (lowerRef.StartsWith("kdmc") || lowerDesc.Contains("kwekwe"))
                return "Kwekwe City Council (KDMC)";
            if (lowerRef.StartsWith("com/") || lowerDesc.Contains("masvingo"))
                return "City of Masvingo";
            if (lowerRef.StartsWith("timb") || lowerDesc.Contains("tobacco industry"))
                return "Tobacco Industry & Marketing Board (TIMB)";
            if (lowerRef.StartsWith("vfcc") || lowerDesc.Contains("victoria falls"))
                return "Victoria Falls City Council (VFCC)";
            if (lowerRef.StartsWith("chit") || lowerDesc.Contains("chitungwiza"))
                return "Chitungwiza Municipality";
            if (lowerRef.StartsWith("gzu") || lowerDesc.Contains("great zimbabwe university"))
                return "Great Zimbabwe University (GZU)";
            if (lowerRef.StartsWith("znr") || lowerRef.StartsWith("zinara") || lowerDesc.Contains("road administration"))
                return "Zimbabwe National Road Administration (ZINARA)";
            if (lowerRef.Contains("agra") || lowerDesc.Contains("alliance for a green revolution"))
                return "Alliance for a Green Revolution in Africa (AGRA)";
            if (lowerRef.Contains("lrfp") || lowerDesc.Contains("unicef"))
                return "UNICEF Zimbabwe";
            if (lowerRef.StartsWith("ama/") || lowerDesc.Contains("agricultural information repository") || lowerDesc.Contains("agricultural marketing authority"))
                return "Agricultural Marketing Authority (AMA)";
            if (lowerRef.StartsWith("acz/") || lowerDesc.Contains("airports company") || lowerDesc.Contains("international airports"))
                return "Airports Company of Zimbabwe (ACZ)";
            if (lowerRef.StartsWith("baz/") || lowerDesc.Contains("broadcasting authority"))
                return "Broadcasting Authority of Zimbabwe (BAZ)";
            if (lowerRef.StartsWith("undp-") || lowerDesc.Contains("undp"))
                return "UNDP Zimbabwe";
            if (lowerRef.StartsWith("eoiunpd") || lowerRef.StartsWith("unpd") || lowerDesc.Contains("united nations peacekeeping"))
                return "United Nations Procurement Division (UNPD)";
            if (lowerRef.StartsWith("ppoly") || lowerDesc.Contains("polytechnic"))
                return "Polytechnic College (Zimbabwe)";
            if (lowerRef.StartsWith("zimra/") || lowerDesc.Contains("revenue authority"))
                return "Zimbabwe Revenue Authority (ZIMRA)";
            if (lowerRef.StartsWith("zetdc/") || lowerDesc.Contains("zetdc") || lowerDesc.Contains("zesa"))
                return "ZETDC / ZESA Holdings";
            if (lowerRef.StartsWith("rea/") || lowerDesc.Contains("rural electrification"))
                return "Rural Electrification Agency (REA)";
            if (lowerRef.StartsWith("potraz/") || lowerDesc.Contains("postal and telecommunications"))
                return "POTRAZ Universal Service Fund";
            if (lowerRef.StartsWith("natpharm/") || lowerDesc.Contains("natpharm"))
                return "NatPharm Zimbabwe";
            if (lowerRef.StartsWith("mofed/") || lowerDesc.Contains("ministry of finance"))
                return "Ministry of Finance & Economic Dev";
            if (lowerRef.StartsWith("coh/") || lowerDesc.Contains("city of harare"))
                return "City of Harare";
            if (lowerDesc.Contains("melfort") || lowerDesc.Contains("goromonzi"))
                return "Goromonzi District Council";

            return "Zimbabwe Public Authority / Commercial Issuer";
        }

        private static string CleanTenderTitle(string desc)
        {
            if (string.IsNullOrWhiteSpace(desc)) return "Procurement Notice";
            var clean = CleanHtml(desc).Replace("show more details...", "").Trim();

            var match = Regex.Match(clean, @"(?:invites\s+Expressions?\s+of\s+Interest\s+\(EOI\)\s+(?:from\s+[^for]+)?for(?:\s+the)?|tenders?\s+are\s+hereby\s+invited\s+for(?:\s+the)?|quotations?\s+are\s+hereby\s+(?:invited|requested)\s+for(?:\s+the)?|proposals?\s+are\s+hereby\s+invited\s+for(?:\s+the)?|bids?\s+are\s+hereby\s+invited\s+from\s+[^for]+for(?:\s+the)?|solicit\s+bids\s+from\s+[^for]+for(?:\s+the)?)\s+(.+)", RegexOptions.IgnoreCase);
            if (match.Success && match.Groups.Count > 1)
            {
                var subject = match.Groups[1].Value.Trim();
                if (subject.Length > 0)
                {
                    var cleanSubject = char.ToUpperInvariant(subject[0]) + subject.Substring(1);
                    if (cleanSubject.Length > 120)
                    {
                        var sentenceEnd = cleanSubject.IndexOfAny(new[] { '.', ';', '\n' });
                        if (sentenceEnd > 20 && sentenceEnd < 120)
                            return cleanSubject.Substring(0, sentenceEnd).Trim();
                        return cleanSubject.Substring(0, 115).Trim() + "...";
                    }
                    return cleanSubject;
                }
            }

            if (clean.Length > 110)
            {
                var sentenceEnd = clean.IndexOfAny(new[] { '.', ';', '\n' });
                if (sentenceEnd > 20 && sentenceEnd < 110)
                    return clean.Substring(0, sentenceEnd).Trim();
                return clean.Substring(0, 105).Trim() + "...";
            }

            return clean;
        }

        private static string CleanHtml(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var decoded = System.Net.WebUtility.HtmlDecode(input);
            // Convert block-level elements to newlines so paragraphs and sections stay structured
            var withBreaks = Regex.Replace(decoded, @"<\s*(?:p|div|br\s*/?|li|tr|h[1-6])[^>]*>", "\n", RegexOptions.IgnoreCase);
            var clean = Regex.Replace(withBreaks, @"<[^>]+>", " ");
            clean = Regex.Replace(clean, @"[ \t\r\f]+", " ");
            clean = Regex.Replace(clean, @"\n\s*\n\s*\n+", "\n\n");
            return clean.Trim();
        }

        private static List<string> ExtractLinks(string? html)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(html)) return list;
            var matches = Regex.Matches(html, @"href=[\""\'](https?://[^\""\']+)[\""\']|https?://[^\s<\""\']+", RegexOptions.IgnoreCase);
            foreach (Match m in matches)
            {
                var val = m.Groups[1].Success ? m.Groups[1].Value : m.Value;
                if (!list.Contains(val)) list.Add(val);
            }
            return list;
        }

        private static List<string> ParseCsvLine(string line)
        {
            var result = new List<string>();
            var inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '\"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            result.Add(current.ToString());
            return result;
        }

        private static DateTime? ParseFlexibleDate(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;
            var clean = input.Trim().Trim('\"');

            // Support South African / Zimbabwean time notation e.g. "2026-10-01 17H00"
            var hMatch = Regex.Match(clean, @"^(\d{4}-\d{2}-\d{2})\s+(\d{1,2})[hH](\d{2})");
            if (hMatch.Success)
            {
                clean = $"{hMatch.Groups[1].Value} {hMatch.Groups[2].Value}:{hMatch.Groups[3].Value}:00";
            }

            string[] formats = {
                "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm",
                "dd-MMM-yyyy", "dd MMM yyyy", "yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-dd", "MM/dd/yyyy", "dd/MM/yyyy", "yyyy/MM/dd", "d MMMM yyyy", "dd MMMM yyyy"
            };

            if (DateTime.TryParseExact(clean, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dtExact))
                return dtExact;

            if (DateTime.TryParse(clean, out var dt))
                return dt;

            return null;
        }

        private static string GetStringOrFirstArray(JsonElement elem, string propName)
        {
            if (!elem.TryGetProperty(propName, out var prop)) return string.Empty;
            if (prop.ValueKind == JsonValueKind.String) return prop.GetString() ?? string.Empty;
            if (prop.ValueKind == JsonValueKind.Array && prop.GetArrayLength() > 0)
            {
                var first = prop[0];
                return first.ValueKind == JsonValueKind.String ? first.GetString() ?? string.Empty : first.ToString();
            }
            return prop.ToString();
        }

        #endregion
    }
}
