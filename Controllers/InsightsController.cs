using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Models;
using ZimbabweTenderAPI.Services;

namespace ZimbabweTenderAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "ProjectManagerAccess")]
    [ApiExplorerSettings(GroupName = "system")]
    public class InsightsController : ControllerBase
    {
        private readonly ITenderScraperService _scraperService;
        private readonly IGeminiAIService _geminiService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<InsightsController> _logger;
        private readonly GeminiConfig _geminiConfig;

        public InsightsController(
            ITenderScraperService scraperService,
            IGeminiAIService geminiService,
            IMemoryCache cache,
            ILogger<InsightsController> logger,
            IConfiguration configuration)
        {
            _scraperService = scraperService;
            _geminiService = geminiService;
            _cache = cache;
            _logger = logger;
            _geminiConfig = configuration.GetSection("Gemini").Get<GeminiConfig>() ?? new GeminiConfig();
        }

        // POST: api/insights/analyze
        [HttpPost("analyze")]
        public async Task<ActionResult<ApiResponse<GeminiInsightResponse>>> AnalyzeTenders(
            [FromBody] GeminiInsightRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.Prompt))
                {
                    return BadRequest(new ApiResponse<GeminiInsightResponse>
                    {
                        Success = false,
                        Message = "Prompt is required"
                    });
                }

                _logger.LogInformation($"Gemini analysis request: {request.Prompt}");
                _logger.LogInformation($"Request settings - IncludeAnalysis: {request.IncludeAnalysis}, IncludeRecommendations: {request.IncludeRecommendations}, Format: {request.OutputFormat}");

                // Get tender data
                List<Tender> tenders;
                var cacheKey = $"insights_tenders_{request.PageNumber}_{request.MaxTenders}_{request.EntityFilter}_{request.CategoryFilter}";

                if (!_cache.TryGetValue(cacheKey, out tenders))
                {
                    if (request.PageNumber.HasValue)
                    {
                        var batch = await _scraperService.ScrapePageAsync(request.PageNumber.Value);
                        tenders = batch.Tenders;
                    }
                    else
                    {
                        // Get multiple pages
                        tenders = await _scraperService.ScrapeMultiplePagesAsync(1, 3);
                    }

                    // Apply filters if specified
                    if (!string.IsNullOrEmpty(request.EntityFilter))
                    {
                        tenders = tenders.Where(t =>
                            t.ProcuringEntity?.Contains(request.EntityFilter, StringComparison.OrdinalIgnoreCase) == true)
                            .ToList();
                    }

                    if (!string.IsNullOrEmpty(request.CategoryFilter))
                    {
                        tenders = tenders.Where(t =>
                            t.CategoryNames?.Any(c => c.Contains(request.CategoryFilter, StringComparison.OrdinalIgnoreCase)) == true)
                            .ToList();
                    }

                    // Limit number of tenders
                    if (request.MaxTenders.HasValue)
                    {
                        tenders = tenders.Take(request.MaxTenders.Value).ToList();
                    }

                    _cache.Set(cacheKey, tenders, TimeSpan.FromMinutes(15));
                }

                // Get AI analysis with configuration options
                var insightResponse = await _geminiService.AnalyzeTendersAsync(
                    tenders,
                    request.Prompt,
                    request.OutputFormat,
                    request.IncludeAnalysis,
                    request.IncludeRecommendations,
                    _geminiConfig);

                return Ok(new ApiResponse<GeminiInsightResponse>
                {
                    Success = true,
                    Message = $"Successfully analyzed {tenders.Count} tenders",
                    Data = insightResponse,
                    TotalCount = insightResponse.RelevantTenders?.Count ?? 0
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing tenders with Gemini");
                return StatusCode(500, new ApiResponse<GeminiInsightResponse>
                {
                    Success = false,
                    Message = $"Error generating insights: {ex.Message}"
                });
            }
        }

        // GET: api/insights/company/{companyProfile}
        [HttpGet("company/{companyProfile}")]
        public async Task<ActionResult> GetCompanyInsights(
            string companyProfile,
            [FromQuery] bool includeAnalysis = true,
            [FromQuery] bool includeRecommendations = true,
            [FromQuery] string format = "html",
            [FromQuery] int pages = 2,
            [FromQuery] string? entityFilter = null,
            [FromQuery] string? categoryFilter = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(companyProfile))
                {
                    return BadRequest(new ApiResponse<string>
                    {
                        Success = false,
                        Message = "Company profile is required"
                    });
                }

                _logger.LogInformation($"Company insights request: {companyProfile}");

                // Get tender data
                var tenders = await _scraperService.ScrapeMultiplePagesAsync(1, pages);

                // Apply filters if specified
                if (!string.IsNullOrEmpty(entityFilter))
                {
                    tenders = tenders.Where(t =>
                        t.ProcuringEntity?.Contains(entityFilter, StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();
                }

                if (!string.IsNullOrEmpty(categoryFilter))
                {
                    tenders = tenders.Where(t =>
                        t.CategoryNames?.Any(c => c.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase)) == true)
                        .ToList();
                }

                // Create prompt
                var prompt = $"Which tenders are most relevant for {companyProfile}? " +
                            "Analyze the tenders and provide specific recommendations for bidding.";

                var insightResponse = await _geminiService.AnalyzeTendersAsync(
                    tenders,
                    prompt,
                    format,
                    includeAnalysis,
                    includeRecommendations,
                    _geminiConfig);

                // Return appropriate format
                if (format.ToLower() == "html")
                {
                    return Content(insightResponse.Analysis, "text/html");
                }
                else if (format.ToLower() == "json")
                {
                    return Ok(new ApiResponse<GeminiInsightResponse>
                    {
                        Success = true,
                        Message = $"Found {insightResponse.RelevantTenders?.Count ?? 0} relevant tenders",
                        Data = insightResponse,
                        TotalCount = insightResponse.RelevantTenders?.Count ?? 0
                    });
                }
                else
                {
                    return Content(insightResponse.Analysis, "text/plain");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting company insights for: {companyProfile}");
                return StatusCode(500, new ApiResponse<string>
                {
                    Success = false,
                    Message = $"Error generating insights: {ex.Message}"
                });
            }
        }

        // GET: api/insights/quick?q=IT company fiscalisation&format=html
        [HttpGet("quick")]
        public async Task<ActionResult> QuickInsights(
            [FromQuery] string q,
            [FromQuery] bool includeAnalysis = true,
            [FromQuery] bool includeRecommendations = true,
            [FromQuery] string format = "html")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<string>
                    {
                        Success = false,
                        Message = "Query is required"
                    });
                }

                _logger.LogInformation($"Quick insights request: {q}");

                // Get recent tenders
                var tenders = await _scraperService.ScrapeMultiplePagesAsync(1, 2);

                var insightResponse = await _geminiService.AnalyzeTendersAsync(
                    tenders,
                    q,
                    format,
                    includeAnalysis,
                    includeRecommendations,
                    _geminiConfig);

                if (format.ToLower() == "html")
                {
                    return Content(insightResponse.Analysis, "text/html");
                }
                else
                {
                    return Ok(new ApiResponse<GeminiInsightResponse>
                    {
                        Success = true,
                        Message = "Quick analysis completed",
                        Data = insightResponse
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in quick insights: {q}");
                return StatusCode(500, new ApiResponse<string>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/insights/category/{category}
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("secret-endpoint")]
        [HttpGet("category/{category}")]
        public async Task<ActionResult<ApiResponse<GeminiInsightResponse>>> GetCategoryInsights(
            string category,
            [FromQuery] string analysisType = "opportunities",
            [FromQuery] bool includeAnalysis = true,
            [FromQuery] bool includeRecommendations = true,
            [FromQuery] string format = "json")
        {
            try
            {
                _logger.LogInformation($"Category insights for: {category}");

                // Get tenders in this category
                var allTenders = await _scraperService.ScrapeMultiplePagesAsync(1, 5);
                var categoryTenders = allTenders
                    .Where(t => t.CategoryNames?.Any(c =>
                        c.Contains(category, StringComparison.OrdinalIgnoreCase)) == true)
                    .ToList();

                if (!categoryTenders.Any())
                {
                    return Ok(new ApiResponse<GeminiInsightResponse>
                    {
                        Success = true,
                        Message = $"No tenders found in category: {category}",
                        Data = new GeminiInsightResponse()
                    });
                }

                var prompt = $"Analyze {category} tenders. " +
                            $"Focus on {analysisType}. " +
                            "Provide insights on competition, requirements, and opportunities.";

                var insightResponse = await _geminiService.AnalyzeTendersAsync(
                    categoryTenders,
                    prompt,
                    format,
                    includeAnalysis,
                    includeRecommendations,
                    _geminiConfig);

                return Ok(new ApiResponse<GeminiInsightResponse>
                {
                    Success = true,
                    Message = $"Analyzed {categoryTenders.Count} {category} tenders",
                    Data = insightResponse,
                    TotalCount = categoryTenders.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting category insights for: {category}");
                return StatusCode(500, new ApiResponse<GeminiInsightResponse>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/insights/dashboard
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("secret-endpoint")]
        [HttpGet("dashboard")]
        public async Task<ActionResult> GetDashboard()
        {
            try
            {
                _logger.LogInformation("Generating insights dashboard");

                // Get recent tenders
                var tenders = await _scraperService.ScrapeMultiplePagesAsync(1, 3);

                // Check if we have real Gemini API key or using mock
                var apiKeyStatus = CheckApiKeyValidity(_geminiConfig.ApiKey);
                var usingMock = apiKeyStatus != "valid";

                var dashboardHtml = @"
<!DOCTYPE html>
<html>
<head>
    <title>Tender Insights Dashboard</title>
    <style>
        body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; margin: 0; padding: 20px; background: #f5f7fa; }
        .header { background: linear-gradient(135deg, #667eea 0%, #764ba2 100%); color: white; padding: 30px; border-radius: 10px; margin-bottom: 30px; }
        .container { display: grid; grid-template-columns: repeat(auto-fit, minmax(300px, 1fr)); gap: 20px; }
        .card { background: white; border-radius: 10px; padding: 20px; box-shadow: 0 4px 6px rgba(0,0,0,0.1); }
        .card h3 { color: #2d3748; margin-top: 0; }
        .insight-box { background: #edf2f7; padding: 15px; border-radius: 8px; margin: 10px 0; }
        .stat { font-size: 2em; font-weight: bold; color: #4c51bf; }
        .refresh-btn { background: #4c51bf; color: white; border: none; padding: 10px 20px; border-radius: 5px; cursor: pointer; margin: 5px; }
        .warning { background: #fff3cd; border: 1px solid #ffc107; color: #856404; padding: 10px; border-radius: 5px; margin: 10px 0; }
        .danger { background: #f8d7da; border: 1px solid #f5c6cb; color: #721c24; padding: 10px; border-radius: 5px; margin: 10px 0; }
        .success { background: #d4edda; border: 1px solid #28a745; color: #155724; padding: 10px; border-radius: 5px; margin: 10px 0; }
        .settings-panel { background: #f8f9fa; padding: 15px; border-radius: 5px; margin: 10px 0; }
        .setting-row { display: flex; align-items: center; margin: 5px 0; }
        .setting-label { width: 150px; font-weight: bold; }
        .setting-control { flex: 1; }
        select, input[type='text'] { width: 100%; padding: 8px; margin: 2px 0; }
        .api-help-link { color: #007bff; text-decoration: underline; cursor: pointer; }
    </style>
</head>
<body>
    <div class='header'>
        <h1>🎯 Tender Insights Dashboard</h1>
        <p>AI-powered analysis of Zimbabwe public tenders</p>
        <div>
            <button class='refresh-btn' onclick='refreshDashboard()'>🔄 Refresh Insights</button>
            <button class='refresh-btn' onclick='showCustomAnalysis()'>🔍 Custom Analysis</button>
            <button class='refresh-btn' onclick='checkApiStatus()' style='background: #6c757d;'>🔑 Check API Status</button>
        </div>
    </div>" +
    (apiKeyStatus == "example" ? @"
    <div class='danger'>
        <strong>❌ Example/Test API Key Detected:</strong> The configured API key appears to be an example/test key.
        <br><small>Current Key: " + (_geminiConfig.ApiKey?.Substring(0, Math.Min(20, _geminiConfig.ApiKey?.Length ?? 0)) + "...") + @"</small>
        <br><strong>Get a real API key:</strong> <span class='api-help-link' onclick='showApiHelp()'>Click here for instructions</span>
    </div>" :
    apiKeyStatus == "invalid" ? @"
    <div class='warning'>
        <strong>⚠️ Invalid API Key Format:</strong> The API key doesn't appear to be in the correct format.
        <br><small>Expected: Starts with 'AIza' and 39+ characters</small>
        <br><small>Current Model: " + _geminiConfig.ModelName + @" | Temperature: " + _geminiConfig.Temperature + @" | Max Tokens: " + _geminiConfig.MaxOutputTokens + @"</small>
    </div>" :
    apiKeyStatus == "missing" ? @"
    <div class='warning'>
        <strong>⚠️ No API Key Configured:</strong> Configure GEMINI_API_KEY in appsettings.json for real AI analysis.
        <br><small>Current Model: " + _geminiConfig.ModelName + @" | Temperature: " + _geminiConfig.Temperature + @" | Max Tokens: " + _geminiConfig.MaxOutputTokens + @"</small>
    </div>" : @"
    <div class='success'>
        <strong>✓ Gemini AI Connected:</strong> Real AI analysis is available.
        <br><small>Model: " + _geminiConfig.ModelName + @" | Temperature: " + _geminiConfig.Temperature + @" | Max Tokens: " + _geminiConfig.MaxOutputTokens + @"</small>
    </div>") + @"
    
    <div id='apiHelpPanel' style='display: none;'>
        <div class='settings-panel'>
            <h3>🔑 How to Get a Real Gemini API Key</h3>
            <ol>
                <li>Go to <a href='https://aistudio.google.com/app/apikey' target='_blank'>Google AI Studio</a></li>
                <li>Sign in with your Google account</li>
                <li>Click 'Create API Key' in the left sidebar</li>
                <li>Select 'Create API Key in new project'</li>
                <li>Copy your new API key (it will start with 'AIza' and be 39+ characters)</li>
                <li>Update your appsettings.json file:
                    <pre><code>{
  ""Gemini"": {
    ""ApiKey"": ""YOUR_REAL_API_KEY_HERE"",
    ""ModelName"": ""gemini-1.5-pro"",
    ""Temperature"": 0.2,
    ""MaxOutputTokens"": 2000
  }
}</code></pre>
                </li>
                <li>Restart your application</li>
            </ol>
            <button class='refresh-btn' onclick='hideApiHelp()' style='background: #95a5a6;'>Close Instructions</button>
        </div>
    </div>
    
    <div id='customAnalysisPanel' style='display: none;'>
        <div class='settings-panel'>
            <h3>🔧 Custom Analysis Settings</h3>
            <div class='setting-row'>
                <div class='setting-label'>Query:</div>
                <div class='setting-control'>
                    <input type='text' id='customQuery' placeholder='e.g., Find tenders for software development company' value='IT company focusing on software development'>
                </div>
            </div>
            <div class='setting-row'>
                <div class='setting-label'>Format:</div>
                <div class='setting-control'>
                    <select id='customFormat'>
                        <option value='html'>HTML (Web Page)</option>
                        <option value='json'>JSON (API)</option>
                        <option value='markdown'>Markdown</option>
                    </select>
                </div>
            </div>
            <div class='setting-row'>
                <div class='setting-label'>Include Analysis:</div>
                <div class='setting-control'>
                    <input type='checkbox' id='customIncludeAnalysis' checked>
                </div>
            </div>
            <div class='setting-row'>
                <div class='setting-label'>Include Recommendations:</div>
                <div class='setting-control'>
                    <input type='checkbox' id='customIncludeRecommendations' checked>
                </div>
            </div>
            <div class='setting-row'>
                <div class='setting-label'>Entity Filter:</div>
                <div class='setting-control'>
                    <input type='text' id='customEntityFilter' placeholder='Filter by procuring entity'>
                </div>
            </div>
            <div class='setting-row'>
                <div class='setting-label'>Category Filter:</div>
                <div class='setting-control'>
                    <input type='text' id='customCategoryFilter' placeholder='Filter by category'>
                </div>
            </div>
            <div class='setting-row'>
                <div class='setting-label'>Max Tenders:</div>
                <div class='setting-control'>
                    <input type='number' id='customMaxTenders' value='10' min='1' max='50'>
                </div>
            </div>
            <div style='margin-top: 15px;'>
                <button class='refresh-btn' onclick='runCustomAnalysis()'>🚀 Run Analysis</button>
                <button class='refresh-btn' onclick='hideCustomAnalysis()' style='background: #95a5a6;'>Cancel</button>
            </div>
        </div>
    </div>
    
    <div class='container'>
        <div class='card'>
            <h3>📊 Quick Stats</h3>
            <div class='stat' id='totalTenders'>Loading...</div>
            <p>Total tenders analyzed</p>
            
            <div class='insight-box'>
                <h4>IT Tenders</h4>
                <div class='stat' id='itTenders'>...</div>
            </div>
            
            <div class='insight-box'>
                <h4>Closing This Week</h4>
                <div class='stat' id='closingSoon'>...</div>
            </div>
            
            <div class='insight-box'>
                <h4>API Status</h4>
                <div class='stat' id='apiStatus' style='font-size: 1.2em;'>" + (apiKeyStatus == "valid" ? "✅ Connected" : "⚠️ Mock") + @"</div>
            </div>
        </div>
        
        <div class='card'>
            <h3>💡 Top Opportunities</h3>
            <div id='opportunities'>
                <p>Loading AI insights...</p>
            </div>
        </div>
        
        <div class='card'>
            <h3>⚙️ Quick Analysis</h3>
            <input type='text' id='aiQuery' placeholder='e.g., Find tenders for software development company' style='width: 100%; padding: 10px; margin-bottom: 10px;'>
            
            <div class='setting-row'>
                <div class='setting-label'>Include Analysis:</div>
                <div class='setting-control'>
                    <input type='checkbox' id='quickIncludeAnalysis' checked>
                </div>
            </div>
            <div class='setting-row'>
                <div class='setting-label'>Include Recommendations:</div>
                <div class='setting-control'>
                    <input type='checkbox' id='quickIncludeRecommendations' checked>
                </div>
            </div>
            
            <button onclick='quickAskAI()' style='background: #38a169; color: white; padding: 10px; border: none; border-radius: 5px; width: 100%; margin-top: 10px;'>Get Insights</button>
            <div id='aiResponse' style='margin-top: 15px;'></div>
        </div>
    </div>
    
    <script>
        async function refreshDashboard() {
            const response = await fetch('/api/insights/dashboard/data');
            const data = await response.json();
            
            document.getElementById('totalTenders').textContent = data.totalTenders;
            document.getElementById('itTenders').textContent = data.itTenders;
            document.getElementById('closingSoon').textContent = data.closingSoon;
            document.getElementById('opportunities').innerHTML = data.opportunities;
        }
        
        function showApiHelp() {
            document.getElementById('apiHelpPanel').style.display = 'block';
        }
        
        function hideApiHelp() {
            document.getElementById('apiHelpPanel').style.display = 'none';
        }
        
        async function checkApiStatus() {
            const response = await fetch('/api/insights/check-api-key');
            const data = await response.json();
            alert('API Status: ' + data.message + '\\nKey Length: ' + data.data.keyLength + '\\nModel: ' + data.data.modelName);
        }
        
        function showCustomAnalysis() {
            document.getElementById('customAnalysisPanel').style.display = 'block';
        }
        
        function hideCustomAnalysis() {
            document.getElementById('customAnalysisPanel').style.display = 'none';
        }
        
        async function runCustomAnalysis() {
            const query = document.getElementById('customQuery').value;
            const format = document.getElementById('customFormat').value;
            const includeAnalysis = document.getElementById('customIncludeAnalysis').checked;
            const includeRecommendations = document.getElementById('customIncludeRecommendations').checked;
            const entityFilter = document.getElementById('customEntityFilter').value;
            const categoryFilter = document.getElementById('customCategoryFilter').value;
            const maxTenders = document.getElementById('customMaxTenders').value;
            
            let url = `/api/insights/analyze-custom?q=${encodeURIComponent(query)}&format=${format}`;
            url += `&includeAnalysis=${includeAnalysis}&includeRecommendations=${includeRecommendations}`;
            if (entityFilter) url += `&entityFilter=${encodeURIComponent(entityFilter)}`;
            if (categoryFilter) url += `&categoryFilter=${encodeURIComponent(categoryFilter)}`;
            url += `&maxTenders=${maxTenders}`;
            
            const response = await fetch(url);
            const html = await response.text();
            
            const newWindow = window.open();
            newWindow.document.write(html);
        }
        
        async function quickAskAI() {
            const query = document.getElementById('aiQuery').value;
            const includeAnalysis = document.getElementById('quickIncludeAnalysis').checked;
            const includeRecommendations = document.getElementById('quickIncludeRecommendations').checked;
            
            if (!query) return;
            
            let url = `/api/insights/quick?q=${encodeURIComponent(query)}&format=html`;
            url += `&includeAnalysis=${includeAnalysis}&includeRecommendations=${includeRecommendations}`;
            
            const response = await fetch(url);
            const html = await response.text();
            
            const newWindow = window.open();
            newWindow.document.write(html);
        }
        
        // Load initial data
        refreshDashboard();
    </script>
</body>
</html>";

                return Content(dashboardHtml, "text/html");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating dashboard");
                return StatusCode(500, new ApiResponse<string>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        private string CheckApiKeyValidity(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return "missing";

            if (apiKey == "AIzaSyCMOL5D9Lsjbde9TbMGpuwaUVy879OGc9A")
                return "example";

            if (apiKey.StartsWith("AIza") && apiKey.Length >= 39)
                return "valid";

            return "invalid";
        }

        // GET: api/insights/dashboard/data

        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpGet("secret-endpoint")]
        [HttpGet("dashboard/data")]
        public async Task<ActionResult<ApiResponse<object>>> GetDashboardData()
        {
            try
            {
                var tenders = await _scraperService.ScrapeMultiplePagesAsync(1, 3);

                // Count IT-related tenders more accurately
                var itKeywords = new[] { "software", "IT", "information technology", "computer",
                                         "system", "application", "digital", "web", "mobile",
                                         "database", "network", "cyber", "fiscalisation" };

                var itTenders = tenders.Count(t =>
                {
                    var title = t.Title?.ToLower() ?? "";
                    var scope = t.Scope?.ToLower() ?? "";
                    var categories = t.CategoryNames?.Select(c => c.ToLower()).ToList() ?? new List<string>();

                    return itKeywords.Any(keyword =>
                        title.Contains(keyword) ||
                        scope.Contains(keyword) ||
                        categories.Any(c => c.Contains(keyword)));
                });

                var closingSoon = tenders.Count(t =>
                    t.ClosingDate.HasValue &&
                    t.ClosingDate.Value <= DateTime.UtcNow.AddDays(7));

                // Get AI insights for opportunities
                var opportunitiesPrompt = "What are the top 3 opportunities in the current tenders? List them briefly.";
                var opportunitiesResponse = await _geminiService.AnalyzeTendersAsync(
                    tenders,
                    opportunitiesPrompt,
                    "html",
                    true,
                    true,
                    _geminiConfig);

                var opportunitiesHtml = opportunitiesResponse.Analysis.Length > 500
                    ? opportunitiesResponse.Analysis.Substring(0, 500) + "..."
                    : opportunitiesResponse.Analysis;

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = "Dashboard data loaded",
                    Data = new
                    {
                        totalTenders = tenders.Count,
                        itTenders = itTenders,
                        closingSoon = closingSoon,
                        aiConfidence = opportunitiesResponse.ConfidenceScore.ToString("P0"),
                        opportunities = opportunitiesHtml
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard data");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/insights/analyze-custom
        [HttpGet("analyze-custom")]
        public async Task<ActionResult> CustomAnalysis(
            [FromQuery] string q,
            [FromQuery] bool includeAnalysis = true,
            [FromQuery] bool includeRecommendations = true,
            [FromQuery] string format = "html",
            [FromQuery] string? entityFilter = null,
            [FromQuery] string? categoryFilter = null,
            [FromQuery] int maxTenders = 10)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return BadRequest(new ApiResponse<string>
                    {
                        Success = false,
                        Message = "Query is required"
                    });
                }

                _logger.LogInformation($"Custom analysis request: {q}");

                // Get tender data
                var tenders = await _scraperService.ScrapeMultiplePagesAsync(1, 3);

                // Apply filters if specified
                if (!string.IsNullOrEmpty(entityFilter))
                {
                    tenders = tenders.Where(t =>
                        t.ProcuringEntity?.Contains(entityFilter, StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();
                }

                if (!string.IsNullOrEmpty(categoryFilter))
                {
                    tenders = tenders.Where(t =>
                        t.CategoryNames?.Any(c => c.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase)) == true)
                        .ToList();
                }

                // Limit number of tenders
                if (maxTenders > 0)
                {
                    tenders = tenders.Take(maxTenders).ToList();
                }

                var insightResponse = await _geminiService.AnalyzeTendersAsync(
                    tenders,
                    q,
                    format,
                    includeAnalysis,
                    includeRecommendations,
                    _geminiConfig);

                if (format.ToLower() == "html")
                {
                    return Content(insightResponse.Analysis, "text/html");
                }
                else if (format.ToLower() == "json")
                {
                    return Ok(new ApiResponse<GeminiInsightResponse>
                    {
                        Success = true,
                        Message = $"Found {insightResponse.RelevantTenders?.Count ?? 0} relevant tenders",
                        Data = insightResponse,
                        TotalCount = insightResponse.RelevantTenders?.Count ?? 0
                    });
                }
                else
                {
                    return Content(insightResponse.Analysis, "text/plain");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error in custom analysis: {q}");
                return StatusCode(500, new ApiResponse<string>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // GET: api/insights/check-api-key
        [HttpGet("check-api-key")]
        public ActionResult<ApiResponse<object>> CheckApiKeyStatus()
        {
            try
            {
                var apiKeyStatus = CheckApiKeyValidity(_geminiConfig.ApiKey);
                var isConfigured = apiKeyStatus == "valid";
                var statusMessage = apiKeyStatus switch
                {
                    "valid" => "Gemini API key is configured and valid",
                    "example" => "Gemini API key appears to be an example/test key",
                    "invalid" => "Gemini API key format is invalid",
                    "missing" => "Gemini API key is not configured",
                    _ => "Unknown API key status"
                };

                return Ok(new ApiResponse<object>
                {
                    Success = true,
                    Message = statusMessage,
                    Data = new
                    {
                        isConfigured = isConfigured,
                        status = apiKeyStatus,
                        modelName = _geminiConfig.ModelName,
                        temperature = _geminiConfig.Temperature,
                        maxOutputTokens = _geminiConfig.MaxOutputTokens,
                        keyLength = _geminiConfig.ApiKey?.Length ?? 0,
                        keyPreview = !string.IsNullOrEmpty(_geminiConfig.ApiKey)
                            ? $"{_geminiConfig.ApiKey.Substring(0, Math.Min(8, _geminiConfig.ApiKey.Length))}..."
                            : null
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking API key status");
                return StatusCode(500, new ApiResponse<object>
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // In InsightsController.cs
        [HttpGet("test-gemini-direct")]
        public async Task<ActionResult> TestGeminiDirect()
        {
            try
            {
                var config = _geminiConfig;
                var testPrompt = "Hello, are you working? Just say 'Yes' if you can read this.";

                _logger.LogInformation($"Testing Gemini API directly with key: {config.ApiKey?.Substring(0, Math.Min(8, config.ApiKey?.Length ?? 0))}...");

                var url = $"https://generativelanguage.googleapis.com/v1beta/models/{config.ModelName}:generateContent?key={config.ApiKey}";

                var requestBody = new
                {
                    contents = new[]
                    {
                new
                {
                    parts = new[]
                    {
                        new { text = testPrompt }
                    }
                }
            },
                    generationConfig = new
                    {
                        temperature = config.Temperature,
                        maxOutputTokens = 100
                    }
                };

                var jsonContent = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var client = new HttpClient();
                var response = await client.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Gemini API test failed: {response.StatusCode} - {errorContent}");

                    return BadRequest(new
                    {
                        status = "error",
                        statusCode = (int)response.StatusCode,
                        statusText = response.StatusCode.ToString(),
                        error = errorContent,
                        keyPreview = config.ApiKey?.Substring(0, Math.Min(8, config.ApiKey?.Length ?? 0)) + "...",
                        keyLength = config.ApiKey?.Length ?? 0
                    });
                }

                var responseJson = await response.Content.ReadAsStringAsync();

                return Ok(new
                {
                    status = "success",
                    message = "Gemini API is working!",
                    keyPreview = config.ApiKey?.Substring(0, Math.Min(8, config.ApiKey?.Length ?? 0)) + "...",
                    keyLength = config.ApiKey?.Length ?? 0,
                    response = JsonSerializer.Deserialize<object>(responseJson)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing Gemini API directly");
                return StatusCode(500, new
                {
                    status = "exception",
                    error = ex.Message,
                    details = ex.ToString()
                });
            }
        }

        // Add this method to InsightsController
        [HttpGet("company-detailed/{companyProfile}")]
        public async Task<ActionResult> GetCompanyDetailedInsights(
            string companyProfile,
            [FromQuery] bool includeAnalysis = true,
            [FromQuery] bool includeRecommendations = true,
            [FromQuery] bool includeDetailContent = true,
            [FromQuery] string format = "html",
            [FromQuery] int pages = 2,
            [FromQuery] int maxTenderDetails = 5,
            [FromQuery] string? entityFilter = null,
            [FromQuery] string? categoryFilter = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(companyProfile))
                {
                    return BadRequest(new ApiResponse<string>
                    {
                        Success = false,
                        Message = "Company profile is required"
                    });
                }

                _logger.LogInformation($"Detailed company insights request: {companyProfile}");

                // Get tender data
                var tenders = await _scraperService.ScrapeMultiplePagesAsync(1, pages);

                // Apply filters if specified
                if (!string.IsNullOrEmpty(entityFilter))
                {
                    tenders = tenders.Where(t =>
                        t.ProcuringEntity?.Contains(entityFilter, StringComparison.OrdinalIgnoreCase) == true)
                        .ToList();
                }

                if (!string.IsNullOrEmpty(categoryFilter))
                {
                    tenders = tenders.Where(t =>
                        t.CategoryNames?.Any(c => c.Contains(categoryFilter, StringComparison.OrdinalIgnoreCase)) == true)
                        .ToList();
                }

                // Create prompt
                var prompt = $"Which tenders are most relevant for {companyProfile}? " +
                            "Analyze the tenders and provide specific recommendations for bidding. " +
                            "You MUST recommend EXACTLY 5 tenders and provide EXACTLY 5 specific recommendations.";

                // Create a method in GeminiAIService that accepts includeDetailContent parameter
                // Or update the interface to include it

                var insightResponse = await _geminiService.AnalyzeTendersDetailedAsync(
                    tenders,
                    prompt,
                    format,
                    includeAnalysis,
                    includeRecommendations,
                    _geminiConfig,
                    includeDetailContent,
                    maxTenderDetails);

                // Return appropriate format
                if (format.ToLower() == "html")
                {
                    return Content(insightResponse.Analysis, "text/html");
                }
                else if (format.ToLower() == "json")
                {
                    return Ok(new ApiResponse<GeminiInsightResponse>
                    {
                        Success = true,
                        Message = $"Found {insightResponse.RelevantTenders?.Count ?? 0} relevant tenders",
                        Data = insightResponse,
                        TotalCount = insightResponse.RelevantTenders?.Count ?? 0
                    });
                }
                else
                {
                    return Content(insightResponse.Analysis, "text/plain");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting detailed company insights for: {companyProfile}");
                return StatusCode(500, new ApiResponse<string>
                {
                    Success = false,
                    Message = $"Error generating insights: {ex.Message}"
                });
            }
        }
    }
}