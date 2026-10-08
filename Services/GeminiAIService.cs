using Google.Cloud.AIPlatform.V1;
using HtmlAgilityPack;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ZimbabweTenderAPI.DTOs;
using ZimbabweTenderAPI.Models;

namespace ZimbabweTenderAPI.Services
{
    public interface IGeminiAIService
    {
        Task<string> GenerateInsightsAsync(string prompt, List<Tender> tenders, string outputFormat = "html",
            bool includeAnalysis = true, bool includeRecommendations = true);
        Task<List<TenderInsight>> FindRelevantTendersAsync(string companyProfile, List<Tender> tenders, int maxResults = 10);

        Task<GeminiInsightResponse> AnalyzeTendersAsync(List<Tender> tenders, string prompt, string outputFormat = "html",
            bool includeAnalysis = true, bool includeRecommendations = true, GeminiConfig? config = null);

        Task<GeminiInsightResponse> AnalyzeTendersDetailedAsync(List<Tender> tenders, string prompt, string outputFormat = "html",
            bool includeAnalysis = true, bool includeRecommendations = true, GeminiConfig? config = null,
            bool includeDetailContent = true, int maxTenderDetails = 5);
    }

    public class GeminiAIService : IGeminiAIService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<GeminiAIService> _logger;
        private readonly HttpClient _httpClient;
        private readonly GeminiConfig _defaultConfig;
        private readonly bool _hasValidApiKey;

        public GeminiAIService(IConfiguration configuration, ILogger<GeminiAIService> logger, HttpClient httpClient)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClient = httpClient;

            // Get configuration from appsettings.json
            _defaultConfig = configuration.GetSection("Gemini").Get<GeminiConfig>() ?? new GeminiConfig();

            _logger.LogInformation(
                "Gemini configuration loaded. Configured: {Configured}, Model: {ModelName}",
                !string.IsNullOrWhiteSpace(_defaultConfig.ApiKey),
                _defaultConfig.ModelName);

            // Check if API key looks valid (real Gemini keys typically start with AIza and are 39+ characters)
            _hasValidApiKey = IsValidApiKey(_defaultConfig.ApiKey);

            if (!_hasValidApiKey)
            {
                _logger.LogWarning("Gemini API key not configured or invalid. Using mock responses.");
                _logger.LogWarning("Gemini key validation failed; no credential details will be logged.");
            }
            else
            {
                _logger.LogInformation($"Gemini API key configured and validated for model: {_defaultConfig.ModelName}");
            }
        }

        private bool IsValidApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
                return false;

            // Trim any whitespace
            apiKey = apiKey.Trim();

            // Check basic format - real Gemini keys start with "AIza" and are 39+ chars
            if (apiKey.StartsWith("AIza") && apiKey.Length >= 39)
            {
                // Allow ALL keys that match the format, even example ones
                // Let the API call itself determine if it's valid
                _logger.LogInformation("Gemini API key format validation passed.");
                return true;
            }

            _logger.LogWarning("Gemini API key format is invalid.");
            return false;
        }

        // Interface implementation: Simple version without detail content
        public async Task<GeminiInsightResponse> AnalyzeTendersAsync(List<Tender> tenders, string prompt, string outputFormat = "html",
            bool includeAnalysis = true, bool includeRecommendations = true, GeminiConfig? config = null)
        {
            // Call the detailed version with default values for detail content
            return await AnalyzeTendersDetailedAsync(
                tenders,
                prompt,
                outputFormat,
                includeAnalysis,
                includeRecommendations,
                config,
                includeDetailContent: false, // Default for non-detailed version
                maxTenderDetails: 0); // No tender details for simple version
        }

        // Interface implementation: Detailed version with tender content
        public async Task<GeminiInsightResponse> AnalyzeTendersDetailedAsync(List<Tender> tenders, string prompt, string outputFormat = "html",
            bool includeAnalysis = true, bool includeRecommendations = true, GeminiConfig? config = null,
            bool includeDetailContent = true, int maxTenderDetails = 5)
        {
            try
            {
                var effectiveConfig = config ?? _defaultConfig;

                if (!_hasValidApiKey)
                {
                    _logger.LogInformation($"Using mock response (API key invalid format). IncludeAnalysis: {includeAnalysis}, IncludeRecommendations: {includeRecommendations}");
                    return await GenerateMockResponse(tenders, prompt, outputFormat, includeAnalysis, includeRecommendations, effectiveConfig);
                }

                // Get tender details if requested
                Dictionary<string, TenderDetail> tenderDetails = null;
                if (includeDetailContent && tenders.Any() && maxTenderDetails > 0)
                {
                    tenderDetails = await GetTenderDetailsForAnalysis(tenders, maxTenderDetails);
                }

                _logger.LogInformation($"Attempting real Gemini API call with key: {effectiveConfig.ApiKey?.Substring(0, Math.Min(12, effectiveConfig.ApiKey?.Length ?? 0))}...");
                var response = await CallGeminiAPIAsync(tenders, prompt, outputFormat, includeAnalysis, includeRecommendations, effectiveConfig, tenderDetails);
                return ParseGeminiResponse(response, tenders, outputFormat, includeAnalysis, includeRecommendations);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing tenders with Gemini");

                // If it's an authentication error, provide specific guidance
                if (ex.Message.Contains("401") || ex.Message.Contains("403") || ex.Message.Contains("PERMISSION_DENIED") || ex.Message.Contains("API key"))
                {
                    _logger.LogError($"Authentication error with Gemini API. Key may be invalid: {_defaultConfig.ApiKey?.Substring(0, Math.Min(8, _defaultConfig.ApiKey?.Length ?? 0))}...");
                }

                return await GenerateMockResponse(tenders, prompt, outputFormat, includeAnalysis, includeRecommendations, config, true);
            }
        }

        private async Task<Dictionary<string, TenderDetail>> GetTenderDetailsForAnalysis(List<Tender> tenders, int maxDetails)
        {
            var tenderDetails = new Dictionary<string, TenderDetail>();

            try
            {
                // Prioritize IT-related tenders for detailed analysis
                var itKeywords = new[] { "software", "IT", "information technology", "computer",
                                 "system", "application", "digital", "web", "mobile",
                                 "database", "network", "cyber", "fiscalisation" };

                // First, identify IT-related tenders
                var itTenders = tenders
                    .Where(t =>
                    {
                        var title = t.Title?.ToLower() ?? "";
                        var scope = t.Scope?.ToLower() ?? "";
                        return itKeywords.Any(keyword => title.Contains(keyword) || scope.Contains(keyword));
                    })
                    .Take(maxDetails)
                    .ToList();

                // If not enough IT tenders, add others
                if (itTenders.Count < maxDetails)
                {
                    var otherTenders = tenders
                        .Except(itTenders)
                        .Take(maxDetails - itTenders.Count)
                        .ToList();
                    itTenders.AddRange(otherTenders);
                }

                // Get details for selected tenders
                var detailTasks = new List<Task<TenderDetail>>();
                foreach (var tender in itTenders.Take(maxDetails))
                {
                    if (!string.IsNullOrEmpty(tender.DetailsUrl))
                    {
                        var task = GetTenderDetailFromUrlAsync(tender.DetailsUrl, tender.TenderId);
                        detailTasks.Add(task);
                    }
                }

                var results = await Task.WhenAll(detailTasks);
                foreach (var detail in results.Where(d => d != null))
                {
                    tenderDetails[detail.TenderId] = detail;
                }

                _logger.LogInformation($"Fetched details for {tenderDetails.Count} tenders");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching tender details for analysis");
            }

            return tenderDetails;
        }

        private async Task<TenderDetail> GetTenderDetailFromUrlAsync(string url, string tenderId)
        {
            try
            {
                var html = await _httpClient.GetStringAsync(url);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                return new TenderDetail
                {
                    TenderId = tenderId,
                    DetailsUrl = url,
                    FullContent = doc.DocumentNode.InnerText?.Trim(),
                    ScrapedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not fetch details for tender {tenderId}: {ex.Message}");
                return null;
            }
        }

        private async Task<string> CallGeminiAPIAsync(List<Tender> tenders, string prompt, string outputFormat,
            bool includeAnalysis, bool includeRecommendations, GeminiConfig config,
            Dictionary<string, TenderDetail> tenderDetails = null)
        {
            // Prepare the prompt with tender data
            var tenderData = FormatTendersForPrompt(tenders);
            var fullPrompt = BuildFullPrompt(prompt, tenderData, outputFormat, includeAnalysis,
                includeRecommendations, config, tenderDetails);

            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = fullPrompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = config.Temperature,
                    topK = 32,
                    topP = 0.95,
                    maxOutputTokens = config.MaxOutputTokens
                }
            };

            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{config.ModelName}:generateContent?key={config.ApiKey}";

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation($"Calling Gemini API for {tenders.Count} tenders with config: Temp={config.Temperature}, Tokens={config.MaxOutputTokens}");
            if (tenderDetails != null)
            {
                _logger.LogInformation($"Including detailed content for {tenderDetails.Count} tenders");
            }

            try
            {
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"Gemini API error: {response.StatusCode} - {errorContent}");
                    throw new HttpRequestException($"Gemini API error: {response.StatusCode}");
                }

                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();

                // Check if response was truncated
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var candidate = candidates[0];
                    if (candidate.TryGetProperty("finishReason", out var finishReason))
                    {
                        var finishReasonStr = finishReason.GetString();
                        if (finishReasonStr == "MAX_TOKENS" || finishReasonStr == "LENGTH")
                        {
                            _logger.LogWarning($"Gemini response was truncated due to token limit. Finish reason: {finishReasonStr}");
                        }
                    }
                }

                _logger.LogInformation($"Gemini API response received successfully. Length: {responseJson.Length} chars");
                return responseJson;
            }
            catch (HttpRequestException httpEx)
            {
                _logger.LogError(httpEx, "HTTP error calling Gemini API");
                throw;
            }
        }

        private string FormatTendersForPrompt(List<Tender> tenders)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Here are the current tenders:");
            sb.AppendLine("==============================");

            int count = 1;
            foreach (var tender in tenders) // Send ALL tenders, not just 20
            {
                sb.AppendLine($"{count}. TENDER ID: {tender.TenderId}");
                sb.AppendLine($"   Reference: {tender.ReferenceNumber}");
                sb.AppendLine($"   Title: {tender.Title}");
                sb.AppendLine($"   Entity: {tender.ProcuringEntity}");
                sb.AppendLine($"   Categories: {string.Join(", ", tender.CategoryNames ?? new List<string>())}");
                sb.AppendLine($"   Scope: {tender.Scope}");
                sb.AppendLine($"   Published: {tender.PublishDate}");
                sb.AppendLine($"   Closes: {tender.ClosingDate}");
                if (!string.IsNullOrEmpty(tender.DetailsUrl))
                {
                    sb.AppendLine($"   Details URL: {tender.DetailsUrl}");
                }
                sb.AppendLine();
                count++;
            }

            _logger.LogInformation($"Formatted {tenders.Count} tenders for Gemini prompt");
            return sb.ToString();
        }

        private string BuildFullPrompt(string userPrompt, string tenderData, string outputFormat,
            bool includeAnalysis, bool includeRecommendations, GeminiConfig config,
            Dictionary<string, TenderDetail> tenderDetails = null)
        {
            var analysisInstructions = new List<string>();

            if (includeAnalysis)
            {
                analysisInstructions.Add("Provide a detailed but concise analysis of the tender landscape");
            }

            if (includeRecommendations)
            {
                analysisInstructions.Add("Provide 5 actionable, specific recommendations for bidding based on tender requirements and scope");
            }

            var formatInstructions = "";
            if (outputFormat.ToLower() == "json")
            {
                formatInstructions = @"
CRITICAL: You MUST return ONLY valid JSON. Do NOT include any markdown, explanations, or text outside the JSON object.

REQUIRED JSON STRUCTURE:
{
  ""analysis"": ""Detailed analysis text here. Focus on IT company capabilities in fiscalisation and software development."",
  ""relevantTenders"": [
    {
      ""tenderId"": ""42511"",
      ""title"": ""PROVISION OF DATA LOSS PREVENTION SOLUTION LICENSE AND SUPPORT FOR TWO (2) YEARS"",
      ""referenceNumber"": ""Reference number if available"",
      ""procuringEntity"": ""ZIMBABWE REVENUE AUTHOURITY"",
      ""detailsUrl"": ""https://egp.praz.org.zw/Indexes/viewLiveTenderDetails/42511"",
      ""relevanceReason"": ""Direct fit for IT company specializing in software security and fiscalisation"",
      ""relevanceScore"": 0.95,
      ""closingDate"": ""2026-01-09T12:00:00"",
      ""recommendedAction"": ""Submit proposal before deadline - high relevance for fiscalisation expertise"",
      ""matchingCriteria"": [""Software"", ""Security"", ""Fiscalisation""]
    }
    // Add 4 more tenders following the same structure
  ],
  ""recommendations"": [
    ""Submit proposal for Tender ID 42511 (ZIMRA DLP) immediately as deadline is approaching"",
    ""Focus on government ministries and parastatals with IT needs"",
    ""Look for fiscalisation-specific requirements in tender documents"",
    ""Consider partnerships for larger IT infrastructure projects"",
    ""Ensure compliance with Zimbabwean ICT regulations and standards""
  ],
  ""confidenceScore"": 0.85,
  ""generatedAt"": ""2025-12-08T00:00:00Z"",
  ""format"": ""json"",
  ""wasTruncated"": false
}

RULES:
1. MUST include EXACTLY 5 tenders in 'relevantTenders' array
2. MUST include EXACTLY 5 recommendations in 'recommendations' array
3. For recommendations: Mention specific tender IDs and deadlines when applicable
4. 'recommendedAction' should include deadline urgency if closing soon
5. Return ONLY the JSON object, nothing else";
            }
            else
            {
                formatInstructions = outputFormat.ToLower() switch
                {
                    "html" => @"
HTML format requirements:
- Clean, professional HTML page
- Header with analysis summary
- Table of EXACTLY 5 most relevant tenders with: Tender ID, Title, Entity, Relevance Score (%), Closing Date, Key Requirements
- View Details button linking to tender URL
- Detailed recommendations section with 5 specific, actionable items
- CSS styling included in <style> tags
- Keep HTML concise and efficient
- Include timestamp: 'Generated: [current datetime]'
- Include debug info: 'DEBUG: Analyzed [X] tenders' at the bottom",
                    "markdown" => @"
Markdown format:
- # Analysis Summary
- ## Top 5 Relevant Tenders (table with details)
- ## 5 Specific Recommendations (based on tender requirements)",
                    _ => "Provide plain text output with 5 tenders and 5 recommendations"
                };
            }

            var tenderDetailsSection = "";
            if (tenderDetails != null && tenderDetails.Any())
            {
                var sb = new StringBuilder();
                sb.AppendLine("\nDETAILED TENDER CONTENT:");
                sb.AppendLine("========================");

                foreach (var kvp in tenderDetails.Take(10))
                {
                    var detail = kvp.Value;
                    sb.AppendLine($"\nTENDER ID: {detail.TenderId}");

                    if (!string.IsNullOrEmpty(detail.FullContent))
                    {
                        // Truncate content to avoid token limit
                        var content = detail.FullContent.Length > 2000
                            ? detail.FullContent.Substring(0, 2000) + "... [truncated]"
                            : detail.FullContent;
                        sb.AppendLine($"CONTENT: {content}");
                    }
                    sb.AppendLine("---");
                }

                tenderDetailsSection = sb.ToString();
            }

            return $@"
You are an expert tender analysis assistant for Zimbabwe's public procurement system.

USER REQUEST: {userPrompt}

TENDER DATA:
{tenderData}

{tenderDetailsSection}

CRITICAL REQUIREMENTS:
1. You MUST recommend EXACTLY 5 tenders - no more, no less
2. For IT companies, focus on: software development, IT services, computer systems, applications, websites, mobile apps, databases, networks, digital solutions, fiscalisation
3. Each tender recommendation MUST include specific requirements analysis
4. Check closing dates and prioritize tenders closing soon
5. Include deadline urgency in recommendations for tenders closing within 7 days
6. {string.Join("\n7. ", analysisInstructions)}
8. Provide EXACTLY 5 specific, actionable recommendations based on actual tender requirements
9. Recommendations should be tailored to the tender content when available
10. For JSON format: Return ONLY valid JSON, no other text

OUTPUT REQUIREMENTS:
{formatInstructions}

ANALYSIS FOCUS:
- Technical requirements and specifications
- Budget considerations
- Evaluation criteria
- Submission requirements
- Competitive landscape
- Relevance to fiscalisation and software development
- Deadline urgency for tenders closing soon

Model: {config.ModelName}
Temperature: {config.Temperature}
Max Tokens: {config.MaxOutputTokens}

IMPORTANT: Return EXACTLY 5 tenders and EXACTLY 5 recommendations. Be specific and actionable.
";
        }

        private GeminiInsightResponse ParseGeminiResponse(string responseJson, List<Tender> tenders, string outputFormat,
            bool includeAnalysis, bool includeRecommendations)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                // Check for truncation
                bool wasTruncated = false;
                string finishReason = null;

                if (root.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0)
                {
                    var candidate = candidates[0];
                    if (candidate.TryGetProperty("finishReason", out var finishReasonElement))
                    {
                        finishReason = finishReasonElement.GetString();
                        wasTruncated = finishReason == "MAX_TOKENS" || finishReason == "LENGTH";
                    }
                }

                var generatedText = root
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                _logger.LogInformation($"Raw AI response (first 500 chars): {generatedText.Substring(0, Math.Min(500, generatedText.Length))}");

                // Special handling for JSON format
                if (outputFormat.ToLower() == "json")
                {
                    // Check if the response is HTML (the AI ignored format instructions)
                    if (generatedText.Contains("<!DOCTYPE html>") || generatedText.Contains("<html>") ||
                        generatedText.Contains("<table>") || generatedText.Contains("</div>"))
                    {
                        _logger.LogWarning("AI returned HTML instead of JSON. Falling back to HTML parsing.");
                        // Parse HTML response as if it was HTML format
                        return ParseHtmlResponseAsJson(generatedText, tenders, wasTruncated);
                    }

                    try
                    {
                        // Clean the response - remove markdown code blocks and trim
                        var cleanedJson = CleanJsonResponse(generatedText);

                        _logger.LogInformation($"Cleaned JSON response (first 500 chars): {cleanedJson.Substring(0, Math.Min(500, cleanedJson.Length))}");

                        // Try to parse the cleaned JSON
                        var jsonResponse = JsonSerializer.Deserialize<GeminiInsightResponse>(cleanedJson);
                        if (jsonResponse != null && jsonResponse.RelevantTenders != null && jsonResponse.RelevantTenders.Count > 0)
                        {
                            // Ensure we have the required fields
                            jsonResponse.Format = "json";
                            jsonResponse.GeneratedAt = DateTime.UtcNow.ToString("o");
                            jsonResponse.ConfidenceScore = jsonResponse.ConfidenceScore > 0 ? jsonResponse.ConfidenceScore : 0.85;
                            jsonResponse.WasTruncated = wasTruncated;

                            _logger.LogInformation($"Successfully parsed JSON response with {jsonResponse.RelevantTenders.Count} tenders and {jsonResponse.Recommendations?.Count ?? 0} recommendations");

                            return jsonResponse;
                        }
                        else
                        {
                            _logger.LogWarning("JSON deserialization returned null or empty tenders. Falling back to HTML parsing.");
                            return ParseHtmlResponseAsJson(generatedText, tenders, wasTruncated);
                        }
                    }
                    catch (JsonException jsonEx)
                    {
                        _logger.LogError(jsonEx, "Failed to parse AI response as JSON. Falling back to HTML parsing.");
                        return ParseHtmlResponseAsJson(generatedText, tenders, wasTruncated);
                    }
                }

                // For HTML format, extract relevant tender insights
                var relevantTenders = ExtractRelevantTenderInsights(generatedText, tenders);

                // Parse confidence from response if available
                var confidenceScore = 0.85;
                if (root.TryGetProperty("usageMetadata", out var usageMetadata))
                {
                    if (usageMetadata.TryGetProperty("totalTokenCount", out var tokenCount))
                    {
                        // Simple confidence based on token usage (more tokens = more detailed analysis)
                        confidenceScore = Math.Min(0.95, 0.7 + (tokenCount.GetInt32() / 2000.0) * 0.25);

                        // Lower confidence if response was truncated
                        if (wasTruncated)
                        {
                            confidenceScore *= 0.7;
                            _logger.LogWarning($"Response was truncated (Finish reason: {finishReason}). Confidence reduced.");
                        }
                    }
                }

                // Add debug info to HTML analysis
                if (outputFormat.ToLower() == "html" && !string.IsNullOrEmpty(generatedText))
                {
                    var debugInfo = $@"
<div style='background: #e8f4f8; border: 1px solid #3498db; padding: 15px; margin: 20px 0; border-radius: 5px;'>
    <h3 style='color: #2c3e50; margin-top: 0;'>🔍 Debug Information</h3>
    <p><strong>Analysis Request Timestamp:</strong> {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")}</p>
    <p><strong>Tenders Analyzed:</strong> {tenders.Count} tenders were sent to Gemini AI</p>
    <p><strong>Relevant Tenders Found:</strong> {relevantTenders.Count}</p>
    <p><strong>Response Confidence:</strong> {confidenceScore:P0}</p>
    <p><strong>AI Model:</strong> {_defaultConfig.ModelName}</p>
    <p><strong>API Status:</strong> {(wasTruncated ? "⚠️ Response was truncated (token limit)" : "✅ Full response received")}</p>
</div>";

                    if (generatedText.Contains("</body>"))
                    {
                        generatedText = generatedText.Replace("</body>", debugInfo + "</body>");
                    }
                    else if (generatedText.Contains("</html>"))
                    {
                        generatedText = generatedText.Replace("</html>", debugInfo + "</html>");
                    }
                    else
                    {
                        generatedText += debugInfo;
                    }
                }

                // Add truncation warning to analysis if needed
                if (wasTruncated && !string.IsNullOrEmpty(generatedText))
                {
                    var truncationWarning = $@"
<div style='background: #fff3cd; border-left: 4px solid #ffc107; padding: 10px; margin: 10px 0;'>
    <strong>⚠️ Note:</strong> This response was truncated due to token limits. 
    Consider reducing the number of tenders analyzed or increasing MaxOutputTokens in configuration.
</div>";

                    if (outputFormat.ToLower() == "html" && generatedText.Contains("</body>"))
                    {
                        // Insert warning before closing body tag
                        generatedText = generatedText.Replace("</body>", truncationWarning + "</body>");
                    }
                    else
                    {
                        generatedText = truncationWarning + generatedText;
                    }
                }

                return new GeminiInsightResponse
                {
                    Analysis = generatedText,
                    Format = outputFormat,
                    GeneratedAt = DateTime.UtcNow.ToString("o"),
                    ConfidenceScore = confidenceScore,
                    RelevantTenders = relevantTenders,
                    WasTruncated = wasTruncated
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing Gemini response");
                return new GeminiInsightResponse
                {
                    Analysis = $"Error parsing response: {ex.Message}. Using mock data instead.",
                    Format = outputFormat,
                    GeneratedAt = DateTime.UtcNow.ToString("o"),
                    ConfidenceScore = 0.5,
                    RelevantTenders = new List<TenderInsight>()
                };
            }
        }

        // Parse HTML response as JSON when AI ignores format instructions
        private GeminiInsightResponse ParseHtmlResponseAsJson(string htmlText, List<Tender> tenders, bool wasTruncated)
        {
            try
            {
                _logger.LogInformation("Parsing HTML response as JSON fallback");

                // Extract tenders from HTML table
                var relevantTenders = new List<TenderInsight>();
                var recommendations = new List<string>();
                var analysis = "";

                // Parse HTML using HtmlAgilityPack
                var htmlDoc = new HtmlDocument();
                htmlDoc.LoadHtml(htmlText);

                // Extract analysis from paragraphs
                var paragraphs = htmlDoc.DocumentNode.SelectNodes("//p");
                if (paragraphs != null)
                {
                    foreach (var p in paragraphs)
                    {
                        var text = p.InnerText.Trim();
                        if (!string.IsNullOrEmpty(text) && text.Length > 50)
                        {
                            analysis += text + " ";
                        }
                    }
                }

                // Extract tenders from table
                var rows = htmlDoc.DocumentNode.SelectNodes("//table//tr[position()>1]");
                if (rows != null)
                {
                    foreach (var row in rows.Take(10))
                    {
                        var cells = row.SelectNodes("td");
                        if (cells != null && cells.Count >= 7)
                        {
                            try
                            {
                                var tenderId = cells[0].InnerText.Trim();
                                var title = cells[1].InnerText.Trim();
                                var entity = cells[2].InnerText.Trim();
                                var relevanceText = cells[3].InnerText.Trim();
                                var closingDateText = cells[4].InnerText.Trim();
                                var keyRequirements = cells[5].InnerText.Trim();

                                // Find the tender in our list
                                var tender = tenders.FirstOrDefault(t => t.TenderId == tenderId);
                                if (tender != null)
                                {
                                    // Parse relevance score (remove %)
                                    double relevanceScore = 0.7;
                                    var match = Regex.Match(relevanceText, @"(\d+)%");
                                    if (match.Success)
                                    {
                                        relevanceScore = double.Parse(match.Groups[1].Value) / 100.0;
                                    }

                                    // Check if closing soon
                                    DateTime? closingDate = null;
                                    if (DateTime.TryParse(closingDateText, out var parsedDate))
                                    {
                                        closingDate = parsedDate;
                                    }

                                    var recommendedAction = "Review tender details";
                                    if (closingDate.HasValue && closingDate.Value <= DateTime.UtcNow.AddDays(7))
                                    {
                                        recommendedAction = $"Submit proposal immediately - closes in {(closingDate.Value - DateTime.UtcNow).Days} days";
                                    }

                                    relevantTenders.Add(new TenderInsight
                                    {
                                        TenderId = tenderId,
                                        Title = title,
                                        ReferenceNumber = tender.ReferenceNumber,
                                        ProcuringEntity = entity,
                                        DetailsUrl = tender.DetailsUrl,
                                        ClosingDate = closingDate,
                                        RelevanceScore = relevanceScore,
                                        RelevanceReason = keyRequirements,
                                        RecommendedAction = recommendedAction,
                                        MatchingCriteria = ExtractMatchingCriteria(tender, analysis + keyRequirements)
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Error parsing table row");
                            }
                        }
                    }
                }

                // Extract recommendations from list
                var listItems = htmlDoc.DocumentNode.SelectNodes("//ol//li | //ul//li");
                if (listItems != null)
                {
                    foreach (var li in listItems.Take(10))
                    {
                        var text = li.InnerText.Trim();
                        if (!string.IsNullOrEmpty(text) && text.Length > 20)
                        {
                            recommendations.Add(text);
                        }
                    }
                }

                // If no recommendations found, generate some based on tenders
                if (recommendations.Count == 0)
                {
                    recommendations = GenerateRecommendationsFromTenders(relevantTenders);
                }

                return new GeminiInsightResponse
                {
                    Analysis = string.IsNullOrEmpty(analysis) ? "Analysis generated from tender data" : analysis.Trim(),
                    RelevantTenders = relevantTenders,
                    Recommendations = recommendations,
                    ConfidenceScore = 0.8,
                    GeneratedAt = DateTime.UtcNow.ToString("o"),
                    Format = "json",
                    WasTruncated = wasTruncated
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing HTML response as JSON");
                return new GeminiInsightResponse
                {
                    Analysis = "Error processing response",
                    RelevantTenders = new List<TenderInsight>(),
                    Recommendations = new List<string>(),
                    ConfidenceScore = 0.5,
                    GeneratedAt = DateTime.UtcNow.ToString("o"),
                    Format = "json",
                    WasTruncated = wasTruncated
                };
            }
        }

        // New helper method to generate recommendations based on tenders
        private List<string> GenerateRecommendationsFromTenders(List<TenderInsight> tenders)
        {
            var recommendations = new List<string>();

            // Add specific recommendations for tenders closing soon
            foreach (var tender in tenders.Where(t => t.ClosingDate.HasValue && t.ClosingDate.Value <= DateTime.UtcNow.AddDays(7)))
            {
                var daysLeft = (tender.ClosingDate.Value - DateTime.UtcNow).Days;
                recommendations.Add($"Submit proposal for Tender ID {tender.TenderId} immediately - closes in {daysLeft} day(s)");
            }

            // Add general recommendations
            var generalRecommendations = new List<string>
            {
                "Focus on government ministries and parastatals with IT needs",
                "Look for fiscalisation-specific requirements in tender documents",
                "Consider partnerships for larger IT infrastructure projects",
                "Prepare case studies of similar software implementations",
                "Ensure compliance with Zimbabwean ICT regulations and standards"
            };

            // Fill up to 5 recommendations
            foreach (var rec in generalRecommendations)
            {
                if (recommendations.Count >= 10) break;
                recommendations.Add(rec);
            }

            return recommendations.Take(10).ToList();
        }

        // New helper method to clean JSON responses
        private string CleanJsonResponse(string jsonText)
        {
            if (string.IsNullOrEmpty(jsonText))
                return jsonText;

            var cleaned = jsonText.Trim();

            // Remove markdown code blocks (```json, ```, etc.)
            if (cleaned.StartsWith("```"))
            {
                // Find the end of the opening code block
                var firstNewline = cleaned.IndexOf('\n');
                if (firstNewline > 0)
                {
                    cleaned = cleaned.Substring(firstNewline + 1);
                }

                // Remove trailing backticks
                var lastBackticks = cleaned.LastIndexOf("```");
                if (lastBackticks > 0)
                {
                    cleaned = cleaned.Substring(0, lastBackticks);
                }
            }

            // Remove any other markdown artifacts
            cleaned = cleaned.Replace("`", "").Trim();

            // Remove any text before the first {
            var firstBrace = cleaned.IndexOf('{');
            if (firstBrace > 0)
            {
                cleaned = cleaned.Substring(firstBrace);
            }

            // Remove any text after the last }
            var lastBrace = cleaned.LastIndexOf('}');
            if (lastBrace >= 0 && lastBrace < cleaned.Length - 1)
            {
                cleaned = cleaned.Substring(0, lastBrace + 1);
            }

            return cleaned.Trim();
        }

        private List<TenderInsight> ExtractRelevantTenderInsights(string analysis, List<Tender> tenders)
        {
            var insights = new List<TenderInsight>();

            // Extract tender IDs mentioned in the analysis
            var mentionedTenderIds = new HashSet<string>();

            // Look for patterns like "TENDER ID: 45026" or "ID: 45333"
            var patterns = new[] {
                @"TENDER ID:\s*(\d+)",
                @"ID:\s*(\d+)",
                @"Tender\s*#?\s*(\d+)",
                @"(\d{4,6})", // Look for 4-6 digit numbers that might be tender IDs
                @"tender\s+(\d{4,6})",
                @"ID\s+(\d{4,6})",
                @"<td>(\d{4,6})</td>" // HTML table cells
            };

            foreach (var pattern in patterns)
            {
                var matches = Regex.Matches(analysis, pattern, RegexOptions.IgnoreCase);
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var potentialId = match.Groups[1].Value.Trim();
                        mentionedTenderIds.Add(potentialId);
                        _logger.LogInformation($"Found potential tender ID in analysis: {potentialId}");
                    }
                }
            }

            _logger.LogInformation($"Found {mentionedTenderIds.Count} potential tender IDs in analysis");

            // Create insights for mentioned tenders
            foreach (var tenderId in mentionedTenderIds)
            {
                var tender = tenders.FirstOrDefault(t => t.TenderId == tenderId);
                if (tender != null)
                {
                    insights.Add(new TenderInsight
                    {
                        TenderId = tender.TenderId,
                        Title = tender.Title,
                        ReferenceNumber = tender.ReferenceNumber,
                        ProcuringEntity = tender.ProcuringEntity,
                        DetailsUrl = tender.DetailsUrl,
                        ClosingDate = tender.ClosingDate,
                        RelevanceScore = CalculateRelevanceScore(tender, analysis),
                        RelevanceReason = "Mentioned in AI analysis",
                        RecommendedAction = GetRecommendedAction(tender),
                        MatchingCriteria = ExtractMatchingCriteria(tender, analysis)
                    });
                    _logger.LogInformation($"Added tender {tenderId} to insights");
                }
                else
                {
                    _logger.LogWarning($"Tender ID {tenderId} found in analysis but not in tender list");
                }
            }

            _logger.LogInformation($"Extracted {insights.Count} relevant tender insights");
            return insights;
        }

        // New helper method to get recommended action based on closing date
        private string GetRecommendedAction(Tender tender)
        {
            if (!tender.ClosingDate.HasValue)
                return "Review tender details";

            var daysUntilClosing = (tender.ClosingDate.Value - DateTime.UtcNow).Days;

            if (daysUntilClosing <= 0)
                return "Deadline has passed";
            else if (daysUntilClosing <= 3)
                return $"Submit proposal URGENTLY - closes in {daysUntilClosing} day(s)";
            else if (daysUntilClosing <= 7)
                return $"Submit proposal soon - closes in {daysUntilClosing} day(s)";
            else if (daysUntilClosing <= 14)
                return $"Prepare and submit proposal - closes in {daysUntilClosing} day(s)";
            else
                return $"Review and plan proposal - closes in {daysUntilClosing} day(s)";
        }

        private List<string> ExtractMatchingCriteria(Tender tender, string analysis)
        {
            var criteria = new List<string>();
            var analysisLower = analysis.ToLower();
            var titleLower = tender.Title?.ToLower() ?? "";
            var scopeLower = tender.Scope?.ToLower() ?? "";

            var keywordCategories = new Dictionary<string, List<string>>
            {
                { "IT/Software", new List<string> { "software", "it", "information technology", "computer", "system", "application" } },
                { "Development", new List<string> { "development", "programming", "coding", "web", "mobile", "app" } },
                { "Infrastructure", new List<string> { "network", "database", "server", "cloud", "hosting", "infrastructure" } },
                { "Services", new List<string> { "service", "maintenance", "support", "consulting", "implementation" } },
                { "Government", new List<string> { "government", "ministry", "department", "authority", "public", "state" } },
                { "Fiscalisation", new List<string> { "fiscalisation", "fiscalization", "tax", "revenue", "zra", "zimra" } },
                { "Security", new List<string> { "security", "cctv", "surveillance", "access control", "data loss prevention", "dlp" } }
            };

            foreach (var category in keywordCategories)
            {
                if (category.Value.Any(keyword =>
                    analysisLower.Contains(keyword) &&
                    (titleLower.Contains(keyword) || scopeLower.Contains(keyword))))
                {
                    criteria.Add(category.Key);
                }
            }

            return criteria;
        }

        private double CalculateRelevanceScore(Tender tender, string analysis)
        {
            double score = 0.5; // Start with neutral score

            // Check for IT keywords in both analysis and tender
            var itKeywords = new[] { "software", "IT", "information technology", "computer",
                                     "system", "application", "digital", "web", "mobile",
                                     "database", "network", "cyber", "fiscalisation",
                                     "fiscalization", "zra", "zimra", "revenue", "tax" };

            var title = tender.Title?.ToLower() ?? "";
            var scope = tender.Scope?.ToLower() ?? "";
            var analysisLower = analysis.ToLower();

            // Check if tender is IT-related
            bool isItRelated = itKeywords.Any(keyword =>
                title.Contains(keyword) ||
                scope.Contains(keyword));

            // Check if analysis mentions IT keywords
            bool analysisMentionsIt = itKeywords.Any(keyword =>
                analysisLower.Contains(keyword));

            // Higher score if both tender and analysis are IT-related
            if (isItRelated && analysisMentionsIt)
                score += 0.4;

            // Lower score for non-IT tenders
            var nonItKeywords = new[] { "vehicle", "car", "truck", "plumbing", "hospital",
                                        "construction", "hardware", "poles", "material" };
            if (nonItKeywords.Any(keyword => title.Contains(keyword)))
                score -= 0.3;

            return Math.Max(0.1, Math.Min(1.0, score));
        }

        private async Task<GeminiInsightResponse> GenerateMockResponse(List<Tender> tenders, string prompt, string outputFormat,
            bool includeAnalysis, bool includeRecommendations, GeminiConfig config, bool apiCallFailed = false)
        {
            _logger.LogInformation($"Generating mock response for prompt: {prompt}");
            _logger.LogInformation($"Config - Model: {config.ModelName}, Temperature: {config.Temperature}");
            _logger.LogInformation($"DEBUG: {tenders.Count} tenders available for mock analysis");

            if (apiCallFailed)
            {
                _logger.LogWarning($"API call failed, showing mock response with error note");
            }

            // Determine if this is an IT/software related query
            var isItQuery = prompt.ToLower().Contains("it") ||
                           prompt.ToLower().Contains("software") ||
                           prompt.ToLower().Contains("technology") ||
                           prompt.ToLower().Contains("fiscalisation") ||
                           prompt.ToLower().Contains("computer");

            // More comprehensive IT-related keyword matching
            var itKeywords = new[] { "software", "IT", "information technology", "computer",
                                     "system", "application", "digital", "web", "mobile",
                                     "database", "network", "cyber", "fiscalisation",
                                     "fiscalization", "programming", "developer", "code", "api", "cloud",
                                     "maintenance", "support", "consulting" };

            var relevantTenders = new List<TenderInsight>();

            if (isItQuery)
            {
                // Only return ACTUALLY IT-related tenders
                relevantTenders = tenders
                    .Where(t =>
                    {
                        var title = t.Title?.ToLower() ?? "";
                        var scope = t.Scope?.ToLower() ?? "";
                        var categories = t.CategoryNames?.Select(c => c.ToLower()).ToList() ?? new List<string>();

                        return itKeywords.Any(keyword =>
                            title.Contains(keyword) ||
                            scope.Contains(keyword) ||
                            categories.Any(c => c.Contains(keyword)));
                    })
                    .Take(10)
                    .Select(t => new TenderInsight
                    {
                        TenderId = t.TenderId,
                        Title = t.Title,
                        ReferenceNumber = t.ReferenceNumber,
                        ProcuringEntity = t.ProcuringEntity,
                        DetailsUrl = t.DetailsUrl,
                        ClosingDate = t.ClosingDate,
                        RelevanceScore = 0.7 + new Random().NextDouble() * 0.2, // 70-90% range
                        RelevanceReason = "IT/Technology related tender",
                        RecommendedAction = GetRecommendedAction(t),
                        MatchingCriteria = ExtractMatchingCriteria(t, prompt)
                    })
                    .ToList();

                _logger.LogInformation($"Found {relevantTenders.Count} IT-related tenders in mock response");
            }
            else
            {
                // For non-IT queries, find relevant tenders based on query
                var queryKeywords = prompt.ToLower()
                    .Split(' ', ',', '.', ';', '!', '?')
                    .Where(w => w.Length > 3)
                    .ToList();

                relevantTenders = tenders
                    .Where(t =>
                    {
                        var searchText = $"{t.Title} {t.Scope} {string.Join(" ", t.CategoryNames ?? new List<string>())}".ToLower();
                        return queryKeywords.Any(keyword => searchText.Contains(keyword));
                    })
                    .Take(10)
                    .Select(t => new TenderInsight
                    {
                        TenderId = t.TenderId,
                        Title = t.Title,
                        ReferenceNumber = t.ReferenceNumber,
                        ProcuringEntity = t.ProcuringEntity,
                        DetailsUrl = t.DetailsUrl,
                        ClosingDate = t.ClosingDate,
                        RelevanceScore = 0.6 + new Random().NextDouble() * 0.3, // 60-90% range
                        RelevanceReason = "Matches query keywords",
                        RecommendedAction = GetRecommendedAction(t),
                        MatchingCriteria = queryKeywords.Where(k =>
                            t.Title?.ToLower().Contains(k) == true ||
                            t.Scope?.ToLower().Contains(k) == true).ToList()
                    })
                    .ToList();
            }

            // Generate analysis based on includeAnalysis flag
            string analysis;
            if (includeAnalysis)
            {
                analysis = isItQuery
                    ? $"Analysis for IT company query: '{prompt}'. Found {relevantTenders.Count} potentially relevant IT tenders out of {tenders.Count} total tenders analyzed."
                    : $"Analysis for query: '{prompt}'. Found {relevantTenders.Count} potentially relevant tenders out of {tenders.Count} total tenders analyzed.";
            }
            else
            {
                analysis = $"Found {relevantTenders.Count} relevant tenders for query: '{prompt}' out of {tenders.Count} total tenders analyzed";
            }

            // Generate recommendations based on includeRecommendations flag
            var recommendations = new List<string>();
            if (includeRecommendations)
            {
                recommendations = GenerateRecommendationsFromTenders(relevantTenders);
            }

            // Generate response based on output format
            if (outputFormat.ToLower() == "html")
            {
                return new GeminiInsightResponse
                {
                    Analysis = GenerateMockHtmlAnalysis(prompt, relevantTenders, isItQuery, includeAnalysis, includeRecommendations, config, _hasValidApiKey, apiCallFailed, tenders.Count),
                    RelevantTenders = relevantTenders,
                    Recommendations = recommendations,
                    ConfidenceScore = relevantTenders.Any() ? 0.75 : 0.0,
                    GeneratedAt = DateTime.UtcNow.ToString("o"),
                    Format = "html"
                };
            }
            else if (outputFormat.ToLower() == "markdown")
            {
                return new GeminiInsightResponse
                {
                    Analysis = GenerateMockMarkdownAnalysis(prompt, relevantTenders, isItQuery, includeAnalysis, includeRecommendations, config, _hasValidApiKey, tenders.Count),
                    RelevantTenders = relevantTenders,
                    Recommendations = recommendations,
                    ConfidenceScore = relevantTenders.Any() ? 0.75 : 0.0,
                    GeneratedAt = DateTime.UtcNow.ToString("o"),
                    Format = "markdown"
                };
            }
            else if (outputFormat.ToLower() == "json")
            {
                // Create proper JSON structure for mock response
                return new GeminiInsightResponse
                {
                    Analysis = analysis,
                    RelevantTenders = relevantTenders,
                    Recommendations = recommendations,
                    ConfidenceScore = relevantTenders.Any() ? 0.75 : 0.0,
                    GeneratedAt = DateTime.UtcNow.ToString("o"),
                    Format = "json",
                    WasTruncated = false
                };
            }
            else
            {
                return new GeminiInsightResponse
                {
                    Analysis = analysis,
                    RelevantTenders = relevantTenders,
                    Recommendations = recommendations,
                    ConfidenceScore = relevantTenders.Any() ? 0.75 : 0.0,
                    GeneratedAt = DateTime.UtcNow.ToString("o"),
                    Format = outputFormat
                };
            }
        }

        private string GenerateMockHtmlAnalysis(string prompt, List<TenderInsight> relevantTenders, bool isItQuery,
     bool includeAnalysis, bool includeRecommendations, GeminiConfig config, bool hasValidApiKey,
     bool apiCallFailed = false, int totalTendersAnalyzed = 0)
        {
            var itSpecificAdvice = isItQuery
                ? "Focus on tenders requiring software development, IT services, or technology solutions."
                : "Review tender requirements carefully to ensure your capabilities match.";

            var warningMessage = apiCallFailed
                ? "<p><em>⚠️ API Call Failed - Check your API key and internet connection</em></p>"
                : !hasValidApiKey
                    ? "<p><em>⚠️ Using Mock Data - Configure valid Gemini API key for real AI analysis</em></p>"
                    : "<p><em>⚠️ Using Mock Data - API key may be invalid</em></p>";

            var apiKeyNote = hasValidApiKey
                ? "<p>API key is configured but appears to be invalid or example key. Get a real key from <a href='https://aistudio.google.com/app/apikey' target='_blank'>Google AI Studio</a>.</p>"
                : "<p>Configure GEMINI_API_KEY in appsettings.json with a valid key from Google AI Studio.</p>";

            // Generate recommendations from tenders
            var recommendations = GenerateRecommendationsFromTenders(relevantTenders);

            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>Tender Analysis - {DateTime.Now:yyyy-MM-dd}</title>
    <meta name='generator' content='Gemini AI (Mock) - {config.ModelName}'>
    <style>
        body {{ font-family: Arial, sans-serif; margin: 20px; line-height: 1.6; }}
        .header {{ background: #2c3e50; color: white; padding: 20px; border-radius: 5px; }}
        .analysis {{ background: #f8f9fa; padding: 15px; border-left: 4px solid #3498db; margin: 20px 0; }}
        .tender-table {{ width: 100%; border-collapse: collapse; margin: 20px 0; }}
        .tender-table th {{ background: #34495e; color: white; padding: 12px; text-align: left; }}
        .tender-table td {{ padding: 10px; border-bottom: 1px solid #ddd; }}
        .tender-table tr:hover {{ background: #f5f5f5; }}
        .relevance-high {{ color: #27ae60; font-weight: bold; }}
        .relevance-medium {{ color: #f39c12; }}
        .relevance-low {{ color: #e74c3c; }}
        .recommendations {{ background: #e8f4fc; padding: 15px; border-radius: 5px; margin: 20px 0; }}
        .footer {{ margin-top: 30px; color: #7f8c8d; font-size: 0.9em; }}
        .info-note {{ background: #fff3cd; padding: 10px; border-radius: 3px; margin: 10px 0; font-size: 0.9em; }}
        .action-btn {{ background: #3498db; color: white; padding: 8px 12px; text-decoration: none; border-radius: 3px; display: inline-block; font-size: 0.9em; }}
        .action-btn:hover {{ background: #2980b9; }}
        .config-info {{ background: #e8f6f3; padding: 10px; border-radius: 3px; margin: 10px 0; font-size: 0.85em; }}
        .api-help {{ background: #d4edda; padding: 15px; border-radius: 5px; margin: 20px 0; }}
        .debug-info {{ background: #e8f4f8; border: 1px solid #3498db; padding: 15px; margin: 20px 0; border-radius: 5px; }}
        .urgent-warning {{ background: #f8d7da; border: 1px solid #f5c6cb; color: #721c24; padding: 8px; border-radius: 3px; margin: 3px 0; font-size: 0.9em; }}
        .soon-warning {{ background: #fff3cd; border: 1px solid #ffc107; color: #856404; padding: 8px; border-radius: 3px; margin: 3px 0; font-size: 0.9em; }}
        .rec-item {{ margin-bottom: 12px; padding-left: 20px; position: relative; }}
        .rec-item::before {{ content: '✓'; color: #28a745; font-weight: bold; position: absolute; left: 0; }}
    </style>
</head>
<body>
    <div class='header'>
        <h1>📊 Tender Analysis Report</h1>
        <p>Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC</p>
        <div class='config-info'>
            <strong>AI Configuration:</strong> Model: {config.ModelName} | Temperature: {config.Temperature} | Max Tokens: {config.MaxOutputTokens}
        </div>
        {warningMessage}
    </div>
    
    <div class='debug-info'>
        <h3 style='color: #2c3e50; margin-top: 0;'>🔍 Debug Information</h3>
        <p><strong>Analysis Request Timestamp:</strong> {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")}</p>
        <p><strong>Tenders Analyzed:</strong> {totalTendersAnalyzed} tenders were analyzed</p>
        <p><strong>Relevant Tenders Found:</strong> {relevantTenders.Count}</p>
        <p><strong>AI Model:</strong> {config.ModelName} (Mock Mode)</p>
        <p><strong>Query:</strong> '{prompt}'</p>
        <p><strong>Analysis Type:</strong> {(isItQuery ? "IT Company Focus" : "General Analysis")}</p>
    </div>
    
    <div class='api-help'>
        <h3>🔑 How to Get a Real Gemini API Key:</h3>
        <ol>
            <li>Go to <a href='https://aistudio.google.com/app/apikey' target='_blank'>Google AI Studio</a></li>
            <li>Sign in with your Google account</li>
            <li>Click 'Create API Key'</li>
            <li>Copy your new API key (starts with AIza and is 39+ characters)</li>
            <li>Update your appsettings.json with the real key</li>
        </ol>
    </div>
    
    <div class='analysis'>
        <h2>Query Analysis</h2>
        <p><strong>User Query:</strong> '{prompt}'</p>
        {(includeAnalysis ? $@"
        <p><strong>Analysis Summary:</strong> Found {relevantTenders.Count} potentially relevant tenders out of {totalTendersAnalyzed} analyzed. {itSpecificAdvice}</p>
        <div class='info-note'>
            <strong>Note:</strong> This is mock analysis. Configure a valid Gemini API key for real AI-powered insights.
        </div>" : "<p><strong>Analysis:</strong> [Analysis disabled in request]</p>")}
    </div>";

            if (relevantTenders.Any())
            {
                html += $@"
    <h2>📋 Relevant Tenders ({relevantTenders.Count} found)</h2>
    <table class='tender-table'>
        <thead>
            <tr>
                <th>Tender ID</th>
                <th>Title</th>
                <th>Procuring Entity</th>
                <th>Relevance</th>
                <th>Closing Date</th>
                <th>Status</th>
                <th>Matching Criteria</th>
                <th>Action</th>
            </tr>
        </thead>
        <tbody>";

                foreach (var tender in relevantTenders)
                {
                    var relevanceClass = tender.RelevanceScore > 0.8 ? "relevance-high" :
                                        tender.RelevanceScore > 0.6 ? "relevance-medium" : "relevance-low";

                    var criteriaText = tender.MatchingCriteria != null && tender.MatchingCriteria.Any()
                        ? string.Join(", ", tender.MatchingCriteria)
                        : "General match";

                    var detailsBtn = !string.IsNullOrEmpty(tender.DetailsUrl)
                        ? $@"<a href='{tender.DetailsUrl}' class='action-btn' target='_blank'>View Details</a>"
                        : "<span class='action-btn' style='background: #6c757d;'>No URL</span>";

                    // Add status warning based on closing date
                    string statusWarning = "";
                    if (tender.ClosingDate.HasValue)
                    {
                        var daysUntilClosing = (tender.ClosingDate.Value - DateTime.UtcNow).Days;
                        if (daysUntilClosing <= 0)
                        {
                            statusWarning = "<div class='urgent-warning'>⚠️ Deadline passed</div>";
                        }
                        else if (daysUntilClosing <= 3)
                        {
                            statusWarning = $"<div class='urgent-warning'>🚨 URGENT: {daysUntilClosing} day(s) left</div>";
                        }
                        else if (daysUntilClosing <= 7)
                        {
                            statusWarning = $"<div class='soon-warning'>⏰ Soon: {daysUntilClosing} day(s) left</div>";
                        }
                        else
                        {
                            statusWarning = $"<div>{daysUntilClosing} day(s) left</div>";
                        }
                    }
                    else
                    {
                        statusWarning = "<div>No closing date</div>";
                    }

                    var closingDateStr = tender.ClosingDate?.ToString("yyyy-MM-dd HH:mm") ?? "N/A";

                    html += $@"
            <tr>
                <td><strong>{tender.TenderId}</strong></td>
                <td>{tender.Title}</td>
                <td>{tender.ProcuringEntity}</td>
                <td class='{relevanceClass}'>{tender.RelevanceScore:P0}</td>
                <td>{closingDateStr}</td>
                <td>{statusWarning}</td>
                <td><small>{criteriaText}</small></td>
                <td>{detailsBtn}</td>
            </tr>";
                }

                html += @"
        </tbody>
    </table>";
            }
            else
            {
                html += $@"
    <div class='info-note' style='background: #f8d7da; border-color: #f5c6cb;'>
        <h3>🔍 No Matching Tenders Found</h3>
        <p>No relevant tenders were found for your query in the current data. Analyzed {totalTendersAnalyzed} tenders.</p>
        <p><strong>Suggestions:</strong></p>
        <ul>
            <li>Try different search terms</li>
            <li>Check if any filters are too restrictive</li>
            <li>Monitor the tender portal for new opportunities</li>
        </ul>
    </div>";
            }

            if (includeRecommendations && recommendations.Any())
            {
                html += $@"
    <div class='recommendations'>
        <h2>💡 Recommendations for Action</h2>
        <p><strong>Based on analysis of {totalTendersAnalyzed} tenders, here are specific actions to take:</strong></p>";

                foreach (var recommendation in recommendations)
                {
                    html += $@"
        <div class='rec-item'>{recommendation}</div>";
                }

                html += @"
    </div>";
            }
            else if (includeRecommendations)
            {
                html += $@"
    <div class='info-note'>
        <h3>💡 General Recommendations</h3>
        <p>No specific recommendations generated. Here are general tips:</p>
        <ul>
            <li>Review tender requirements thoroughly before submission</li>
            <li>Ensure all documentation is complete and accurate</li>
            <li>Submit proposals well before deadlines</li>
            <li>Follow up after submission if permitted</li>
        </ul>
    </div>";
            }

            // Add tender statistics
            var urgentTenders = relevantTenders.Count(t =>
                t.ClosingDate.HasValue &&
                (t.ClosingDate.Value - DateTime.UtcNow).Days <= 3);
            var soonTenders = relevantTenders.Count(t =>
                t.ClosingDate.HasValue &&
                (t.ClosingDate.Value - DateTime.UtcNow).Days > 3 &&
                (t.ClosingDate.Value - DateTime.UtcNow).Days <= 7);

            html += $@"
    <div class='info-note'>
        <h3>📈 Tender Statistics</h3>
        <p><strong>Priority Analysis:</strong></p>
        <ul>
            <li>Total Relevant Tenders: {relevantTenders.Count}</li>
            <li>Urgent (≤3 days): {urgentTenders} tender(s)</li>
            <li>Closing Soon (4-7 days): {soonTenders} tender(s)</li>
            <li>Future Opportunities: {relevantTenders.Count - urgentTenders - soonTenders} tender(s)</li>
        </ul>
    </div>";

            html += $@"
    <div class='footer'>
        <p><strong>Analysis Settings:</strong> Include Analysis: {includeAnalysis} | Include Recommendations: {includeRecommendations} | Format: HTML | Model: {config.ModelName}</p>
        {apiKeyNote}
        <p><strong>Important Notes:</strong></p>
        <ul>
            <li>Always verify tender details from official sources before bidding</li>
            <li>Check for any amendments or extensions to closing dates</li>
            <li>Ensure compliance with all submission requirements</li>
            <li>This is mock data - enable real AI for accurate analysis</li>
        </ul>
        <p>© {DateTime.Now.Year} Zimbabwe Tender Insights | Generated at {DateTime.UtcNow.ToString("HH:mm:ss UTC")} | Analysis ID: {Guid.NewGuid().ToString().Substring(0, 8)}</p>
    </div>
</body>
</html>";

            return html;
        }

        private string GenerateMockMarkdownAnalysis(string prompt, List<TenderInsight> relevantTenders, bool isItQuery,
            bool includeAnalysis, bool includeRecommendations, GeminiConfig config, bool hasValidApiKey, int totalTendersAnalyzed)
        {
            // Generate recommendations from tenders
            var recommendations = GenerateRecommendationsFromTenders(relevantTenders);

            var markdown = $@"# Tender Analysis Report
**Generated:** {DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC
**AI Model:** {config.ModelName} (Mock)
**Temperature:** {config.Temperature}
**Max Tokens:** {config.MaxOutputTokens}
**Status:** {(hasValidApiKey ? "⚠️ Using Mock Data - API call failed or returned error" : "⚠️ Using Mock Data - Configure valid Gemini API key")}

## 🔍 Debug Information
- **Analysis Request Timestamp:** {DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")}
- **Tenders Analyzed:** {totalTendersAnalyzed} tenders
- **Relevant Tenders Found:** {relevantTenders.Count}
- **AI Model:** {config.ModelName} (Mock Mode)
- **Query:** '{prompt}'
- **Analysis Type:** {(isItQuery ? "IT Company Focus" : "General Analysis")}

## How to Get a Real Gemini API Key:
1. Go to [Google AI Studio](https://aistudio.google.com/app/apikey)
2. Sign in with your Google account
3. Click 'Create API Key'
4. Copy your new API key (starts with AIza and is 39+ characters)
5. Update your appsettings.json with the real key

## Query Analysis
**User Query:** '{prompt}'

";

            if (includeAnalysis)
            {
                markdown += $@"**Analysis Summary:** Found {relevantTenders.Count} potentially relevant tenders out of {totalTendersAnalyzed} analyzed.
{(isItQuery ? "**Focus:** IT/Software/Technology related tenders" : "**Focus:** General tender analysis")}

**Note:** This is mock analysis. Configure a valid Gemini API key for real AI-powered insights.

";
            }

            if (relevantTenders.Any())
            {
                // Calculate priority counts
                var urgentTenders = relevantTenders.Count(t =>
                    t.ClosingDate.HasValue &&
                    (t.ClosingDate.Value - DateTime.UtcNow).Days <= 3);
                var soonTenders = relevantTenders.Count(t =>
                    t.ClosingDate.HasValue &&
                    (t.ClosingDate.Value - DateTime.UtcNow).Days > 3 &&
                    (t.ClosingDate.Value - DateTime.UtcNow).Days <= 7);

                markdown += $"## 📊 Tender Statistics\n\n";
                markdown += $"- **Total Relevant Tenders:** {relevantTenders.Count}\n";
                markdown += $"- **🚨 Urgent (≤3 days):** {urgentTenders} tender(s)\n";
                markdown += $"- **⏰ Closing Soon (4-7 days):** {soonTenders} tender(s)\n";
                markdown += $"- **📅 Future Opportunities:** {relevantTenders.Count - urgentTenders - soonTenders} tender(s)\n\n";

                markdown += $"## 📋 Relevant Tenders ({relevantTenders.Count} found)\n\n";
                markdown += "| Tender ID | Title | Procuring Entity | Relevance | Closing Date | Status | Matching Criteria |\n";
                markdown += "|-----------|-------|------------------|-----------|--------------|--------|-------------------|\n";

                foreach (var tender in relevantTenders)
                {
                    var criteriaText = tender.MatchingCriteria != null && tender.MatchingCriteria.Any()
                        ? string.Join(", ", tender.MatchingCriteria)
                        : "General match";

                    // Add status indicator
                    string statusIndicator = "";
                    if (tender.ClosingDate.HasValue)
                    {
                        var daysUntilClosing = (tender.ClosingDate.Value - DateTime.UtcNow).Days;
                        if (daysUntilClosing <= 0)
                        {
                            statusIndicator = "❌ Deadline passed";
                        }
                        else if (daysUntilClosing <= 3)
                        {
                            statusIndicator = $"🚨 {daysUntilClosing}d left";
                        }
                        else if (daysUntilClosing <= 7)
                        {
                            statusIndicator = $"⏰ {daysUntilClosing}d left";
                        }
                        else
                        {
                            statusIndicator = $"📅 {daysUntilClosing}d left";
                        }
                    }
                    else
                    {
                        statusIndicator = "❓ No date";
                    }

                    var closingDateStr = tender.ClosingDate?.ToString("yyyy-MM-dd") ?? "N/A";

                    markdown += $"| {tender.TenderId} | {tender.Title.Replace("|", "\\|")} | {tender.ProcuringEntity} | {tender.RelevanceScore:P0} | {closingDateStr} | {statusIndicator} | {criteriaText} |\n";
                }
                markdown += "\n";
            }
            else
            {
                markdown += $@"## ⚠️ No Matching Tenders Found

No relevant tenders were found for your query in the current data. Analyzed {totalTendersAnalyzed} tenders.

**Suggestions:**
- Try different search terms
- Check if any filters are too restrictive
- Monitor the tender portal for new opportunities

";
            }

            if (includeRecommendations && recommendations.Any())
            {
                markdown += "## 💡 Recommendations for Action\n\n";
                markdown += $"**Based on analysis of {totalTendersAnalyzed} tenders, here are specific actions to take:**\n\n";

                int recNumber = 1;
                foreach (var recommendation in recommendations)
                {
                    markdown += $"{recNumber}. **{recommendation}**\n";
                    recNumber++;
                }
                markdown += "\n";
            }
            else if (includeRecommendations)
            {
                markdown += @"## 💡 General Recommendations

**No specific recommendations generated. Here are general tips:**

1. Review tender requirements thoroughly before submission
2. Ensure all documentation is complete and accurate
3. Submit proposals well before deadlines
4. Follow up after submission if permitted
5. Maintain records of all submissions and responses

";
            }

            markdown += $@"---
**Analysis Settings:** Include Analysis: {includeAnalysis} | Include Recommendations: {includeRecommendations} | Format: Markdown
**Total Tenders Analyzed:** {totalTendersAnalyzed}
**Analysis Completed:** {DateTime.UtcNow:HH:mm:ss} UTC
**Analysis ID:** {Guid.NewGuid().ToString().Substring(0, 8).ToUpper()}

### Important Notes:
- Always verify tender details from official sources before bidding
- Check for any amendments or extensions to closing dates
- Ensure compliance with all submission requirements
- This is mock data - enable real AI for accurate analysis

### How to Enable Real AI Analysis:
1. Get a Gemini API key from [Google AI Studio](https://aistudio.google.com/app/apikey)
2. Update appsettings.json with:
   ```json
   ""Gemini"": {{
     ""ApiKey"": ""YOUR_KEY_HERE"",
     ""ModelName"": ""gemini-1.5-pro""
   }}
";
        
    return markdown;
        }
        public async Task<string> GenerateInsightsAsync(string prompt, List<Tender> tenders, string outputFormat = "html",
            bool includeAnalysis = true, bool includeRecommendations = true)
        {
            var response = await AnalyzeTendersAsync(tenders, prompt, outputFormat, includeAnalysis, includeRecommendations);
            return response.Analysis;
        }

        public async Task<List<TenderInsight>> FindRelevantTendersAsync(string companyProfile, List<Tender> tenders, int maxResults = 10)
        {
            var prompt = $"Find tenders relevant for a company with this profile: {companyProfile}";
            var response = await AnalyzeTendersAsync(tenders, prompt, "json", true, true);
            return response.RelevantTenders.Take(maxResults).ToList();
        }
    }
}
