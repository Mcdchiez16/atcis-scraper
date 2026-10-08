using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ZimbabweTenderAPI.Services;

/// <summary>Scraper-only process. No application controllers, local DB, or legacy auth.</summary>
public static class SupabaseScraperHost
{
    public static async Task RunAsync(WebApplicationBuilder builder)
    {
        // The legacy appsettings define two API ports. Scraper mode has a
        // separate, loopback-only health endpoint and needs no HTTPS certificate.
        builder.WebHost.ConfigureKestrel(options => options.Configure(new ConfigurationBuilder().Build()));
        var port = Environment.GetEnvironmentVariable("PORT");
        var defaultUrl = string.IsNullOrEmpty(port) ? "http://127.0.0.1:8097" : $"http://0.0.0.0:{port}";
        builder.WebHost.UseUrls(builder.Configuration["Scraper:ListenUrl"] ?? defaultUrl);
        builder.Services.AddMemoryCache();
        builder.Services.AddHttpClient<ITenderScraperService, TenderScraperService>();
        builder.Services.AddHttpClient<IZambiaTenderScraperService, ZambiaTenderScraperService>();
        builder.Services.AddHttpClient<IMultiSourceProcurementService, MultiSourceProcurementService>();
        builder.Services.AddHttpClient<SupabaseScraperWorker>();
        builder.Services.AddHostedService<SupabaseScraperWorker>();
        var app = builder.Build();
        app.MapGet("/health", () => Results.Ok(new { service = "atcis-scraper", mode = "supabase" }));
        await app.RunAsync();
    }
    public static string? ResolveConfig(IConfiguration config, string key, params string[] alternatives)
    {
        var val = config[key];
        if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

        foreach (var alt in alternatives)
        {
            val = config[alt];
            if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

            val = Environment.GetEnvironmentVariable(alt);
            if (!string.IsNullOrWhiteSpace(val)) return val.Trim();
        }

        val = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

        val = Environment.GetEnvironmentVariable(key.Replace(":", "__"));
        if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

        val = Environment.GetEnvironmentVariable(key.Replace(":", "_"));
        if (!string.IsNullOrWhiteSpace(val)) return val.Trim();

        return null;
    }

    public static int ResolveInt(IConfiguration config, string key, int defaultValue, params string[] alternatives)
    {
        var raw = ResolveConfig(config, key, alternatives);
        return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }
}

public sealed class SupabaseScraperWorker : BackgroundService
{
    private readonly HttpClient _client;
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _config;
    private readonly ILogger<SupabaseScraperWorker> _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private DateTimeOffset _sessionExpires = DateTimeOffset.MinValue;
    private readonly string _supabaseUrl;
    private readonly string _publishableKey;
    private readonly string? _secretKey;
    private readonly string _scraperEmail;
    private readonly string? _scraperPassword;

    public SupabaseScraperWorker(HttpClient client, IServiceScopeFactory scopes, IConfiguration config,
        ILogger<SupabaseScraperWorker> logger, IHostApplicationLifetime lifetime)
    {
        _client = client;
        _scopes = scopes;
        _config = config;
        _logger = logger;
        _lifetime = lifetime;

        _supabaseUrl = SupabaseScraperHost.ResolveConfig(config, "Supabase:Url", "SUPABASE_URL", "Supabase__Url", "Supabase_Url", "NEXT_PUBLIC_SUPABASE_URL") 
            ?? "https://pqqymbdbkwltzydymild.supabase.co";

        _publishableKey = SupabaseScraperHost.ResolveConfig(config, "Supabase:PublishableKey", "SUPABASE_PUBLISHABLE_KEY", "Supabase__PublishableKey", "Supabase_PublishableKey", "SUPABASE_ANON_KEY", "NEXT_PUBLIC_SUPABASE_ANON_KEY", "NEXT_PUBLIC_SUPABASE_PUBLISHABLE_KEY")
            ?? string.Empty;

        _secretKey = SupabaseScraperHost.ResolveConfig(config, "Supabase:SecretKey", "SUPABASE_SECRET_KEY", "Supabase__SecretKey", "Supabase_SecretKey", "SUPABASE_SERVICE_ROLE_KEY", "SERVICE_ROLE_KEY");

        _scraperEmail = SupabaseScraperHost.ResolveConfig(config, "Supabase:ScraperEmail", "SUPABASE_SCRAPER_EMAIL", "Supabase__ScraperEmail", "Supabase_ScraperEmail", "SCRAPER_EMAIL")
            ?? "scraper@atcis.internal";

        _scraperPassword = SupabaseScraperHost.ResolveConfig(config, "Supabase:ScraperPassword", "SUPABASE_SCRAPER_PASSWORD", "Supabase__ScraperPassword", "Supabase_ScraperPassword", "SCRAPER_PASSWORD");

        var requestKey = !string.IsNullOrWhiteSpace(_secretKey) ? _secretKey : _publishableKey;
        if (string.IsNullOrWhiteSpace(requestKey))
        {
            throw new InvalidOperationException(
                "Supabase credentials are missing. Set SUPABASE_SECRET_KEY, or set SUPABASE_PUBLISHABLE_KEY with the scraper account credentials.");
        }

        _client.BaseAddress = new Uri(_supabaseUrl.TrimEnd('/') + "/");
        _client.DefaultRequestHeaders.Add("apikey", requestKey);
        _client.Timeout = TimeSpan.FromSeconds(60);

        _logger.LogInformation("SupabaseScraperWorker initialized. URL: {Url}, Email: {Email}, UsesServiceKey: {UsesKey}", 
            _supabaseUrl, _scraperEmail, !string.IsNullOrWhiteSpace(_secretKey));
    }

    protected override async Task ExecuteAsync(CancellationToken cancellation)
    {
        var nextCycle = DateTimeOffset.MinValue;
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                await Authenticate(cancellation);
                var requests = await _client.GetFromJsonAsync<JsonArray>("rest/v1/scrape_requests?status=eq.pending&order=created_at&limit=1", cancellation);
                if (requests?.Count > 0)
                {
                    var request = requests[0]!;
                    var id = request["id"]!.GetValue<string>();
                    using var claim = new HttpRequestMessage(HttpMethod.Patch, $"rest/v1/scrape_requests?id=eq.{id}&status=eq.pending");
                    claim.Headers.Add("Prefer", "return=representation");
                    claim.Content = JsonContent.Create(new { status = "running", updated_at = DateTimeOffset.UtcNow });
                    using var claimed = await _client.SendAsync(claim, cancellation);
                    claimed.EnsureSuccessStatusCode();
                    var claimedRows = await claimed.Content.ReadFromJsonAsync<JsonArray>(cancellationToken: cancellation);
                    if (claimedRows?.Count > 0)
                    {
                        try
                        {
                            var count = await Scrape(request["pages"]!.GetValue<int>(), cancellation);
                            await UpdateRequest(id, "completed", $"Published {count} tender records", cancellation);
                            var interval = SupabaseScraperHost.ResolveInt(_config, "Scraper:IntervalMinutes", 10, "SCRAPER_INTERVAL_MINUTES", "Scraper__IntervalMinutes", "Scraper_IntervalMinutes");
                            nextCycle = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, interval));
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            await UpdateRequest(id, "failed", "Scraping failed; inspect the scraper logs", cancellation);
                            throw;
                        }
                    }
                }
                if (DateTimeOffset.UtcNow >= nextCycle)
                {
                    var pages = SupabaseScraperHost.ResolveInt(_config, "Scraper:Pages", 50, "SCRAPER_PAGES", "Scraper__Pages", "Scraper_Pages");
                    var interval = SupabaseScraperHost.ResolveInt(_config, "Scraper:IntervalMinutes", 10, "SCRAPER_INTERVAL_MINUTES", "Scraper__IntervalMinutes", "Scraper_IntervalMinutes");
                    await Scrape(Math.Clamp(pages, 1, 50), cancellation);
                    nextCycle = DateTimeOffset.UtcNow.AddMinutes(Math.Max(1, interval));
                }
                if (_config.GetValue("Scraper:RunOnce", false)) { _lifetime.StopApplication(); return; }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Supabase scraping cycle failed; existing records have been retained");
                if (_config.GetValue("Scraper:RunOnce", false)) { Environment.ExitCode = 1; _lifetime.StopApplication(); return; }
            }
            await Task.Delay(TimeSpan.FromSeconds(30), cancellation);
        }
    }

    private async Task Authenticate(CancellationToken cancellation)
    {
        if (_sessionExpires > DateTimeOffset.UtcNow.AddMinutes(5)) return;

        // If a Supabase Service Role Key is configured, use it directly (bypasses RLS & avoids auth session expiry)
        if (!string.IsNullOrWhiteSpace(_secretKey))
        {
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _secretKey);
            _sessionExpires = DateTimeOffset.MaxValue;
            _logger.LogInformation("Successfully configured scraper session using Supabase Service Role Key");
            return;
        }

        var email = _scraperEmail;
        var pwd = _scraperPassword ?? SupabaseScraperHost.ResolveConfig(_config, "Supabase:ScraperPassword", "SUPABASE_SCRAPER_PASSWORD", "Supabase__ScraperPassword", "Supabase_ScraperPassword", "SCRAPER_PASSWORD");

        if (string.IsNullOrWhiteSpace(pwd))
        {
            _logger.LogWarning("Supabase scraper password or secret key is not set. Please ensure SUPABASE_SECRET_KEY or SUPABASE_SCRAPER_PASSWORD is set in environment variables.");
            throw new InvalidOperationException("Supabase authentication credentials (SUPABASE_SECRET_KEY or SUPABASE_SCRAPER_PASSWORD) are required");
        }

        using var response = await _client.PostAsJsonAsync("auth/v1/token?grant_type=password", new
        {
            email = email, password = pwd
        }, cancellation);
        response.EnsureSuccessStatusCode();
        var session = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken: cancellation);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session!["access_token"]!.GetValue<string>());
        _sessionExpires = DateTimeOffset.UtcNow.AddSeconds(session["expires_in"]?.GetValue<int>() ?? 3600);
        _logger.LogInformation("Successfully authenticated scraper session as {Email}", email);
    }

    private async Task UpdateRequest(string id, string status, string message, CancellationToken cancellation)
    {
        using var response = await _client.PatchAsJsonAsync($"rest/v1/scrape_requests?id=eq.{id}", new { status, message, updated_at = DateTimeOffset.UtcNow }, cancellation);
        response.EnsureSuccessStatusCode();
    }

    private static string StableId(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private async Task Upsert(string table, List<JsonObject> rows, CancellationToken cancellation)
    {
        foreach (var batch in rows.GroupBy(r => r["id"]!.GetValue<string>()).Select(g => g.Last()).Chunk(100))
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, $"rest/v1/{table}?on_conflict=id");
            message.Headers.Add("Prefer", "resolution=merge-duplicates,return=minimal");
            message.Content = JsonContent.Create(batch);
            using var response = await _client.SendAsync(message, cancellation);
            response.EnsureSuccessStatusCode();
        }
    }

    private async Task<int> Scrape(int pages, CancellationToken cancellation)
    {
        using var scope = _scopes.CreateScope();
        var scraper = scope.ServiceProvider.GetRequiredService<ITenderScraperService>();
        var multi = scope.ServiceProvider.GetRequiredService<IMultiSourceProcurementService>();
        var totalAvailable = await scraper.GetTotalPagesAsync();
        var pagesToScrape = totalAvailable > 0 ? Math.Min(50, Math.Max(pages, totalAvailable)) : Math.Max(pages, 45);
        _logger.LogInformation("Starting scrape of all {Pages} PRAZ pages (available on PRAZ: {Total}) and regional sources", pagesToScrape, totalAvailable);
        var tenders = await scraper.ScrapeMultiplePagesAsync(1, pagesToScrape).WaitAsync(cancellation);
        var rows = new List<JsonObject>();
        foreach (var tender in tenders)
        {
            if (string.IsNullOrWhiteSpace(tender.TenderId)) continue;
            var payload = JsonSerializer.SerializeToNode(tender, JsonOptions)!.AsObject();
            var id = "praz:" + tender.TenderId;
            payload["id"] = id;
            payload["countryCode"] = "ZW";
            payload["sourcePortal"] = "PRAZ e-GP";
            TenderClassificationRules.Apply(payload);
            rows.Add(new JsonObject { ["id"] = id, ["source"] = "praz", ["source_id"] = tender.TenderId,
                ["country"] = "ZW", ["status"] = tender.ClosingDate < DateTime.UtcNow ? "closed" : "live", ["payload"] = payload,
                ["scraped_at"] = DateTimeOffset.UtcNow });
        }
        // Publish each source before requesting the next so one source's outage
        // does not discard successfully scraped notices.
        await Upsert("tenders", rows, cancellation);
        var opportunities = await multi.GetUnifiedProcurementFeedAsync(country: "all", limit: 200).WaitAsync(cancellation);
        var external = new List<JsonObject>();
        foreach (var tender in opportunities)
        {
            var source = tender.SourcePortal.Trim().ToLowerInvariant();
            var sourceId = !string.IsNullOrWhiteSpace(tender.TenderId) ? tender.TenderId : StableId(tender.SourceUrl + "|" + tender.Title);
            var id = source + ":" + sourceId;
            var country = tender.Country.Contains("Zimbabwe", StringComparison.OrdinalIgnoreCase) ? "ZW" : tender.Country.Contains("Zambia", StringComparison.OrdinalIgnoreCase) ? "ZM" : "ALL";
            var payload = JsonSerializer.SerializeToNode(tender, JsonOptions)!.AsObject();
            payload["id"] = id;
            payload["countryCode"] = country;
            TenderClassificationRules.Apply(payload);
            external.Add(new JsonObject { ["id"] = id, ["source"] = source, ["source_id"] = sourceId,
                ["country"] = country, ["status"] = tender.ClosingDate < DateTime.UtcNow ? "closed" : "live", ["payload"] = payload,
                ["scraped_at"] = DateTimeOffset.UtcNow });
        }

        // Run ZPPA, AfDB, OnlineTenders, UNGM, and World Bank scraper processes to sync latest live notices and official attachments
        foreach (var scriptName in new[] { "scrape_zppa.py", "scrape_afdb.py", "scrape_onlinetenders.py", "scrape_ungm.py", "scrape_worldbank.py" })
        {
            try
            {
                var candidates = new[]
                {
                    Path.Combine(Directory.GetCurrentDirectory(), "..", "scripts", scriptName),
                    Path.Combine(Directory.GetCurrentDirectory(), "scripts", scriptName),
                    Path.Combine("/scripts", scriptName),
                    Path.Combine(AppContext.BaseDirectory, "..", "scripts", scriptName),
                    Path.Combine(AppContext.BaseDirectory, "scripts", scriptName)
                };
                var scriptPath = candidates.FirstOrDefault(File.Exists);
                if (scriptPath != null)
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "python3",
                        Arguments = $"\"{scriptPath}\"",
                        WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(scriptPath))!,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = System.Diagnostics.Process.Start(psi);
                    if (p != null)
                    {
                        var stderrTask = p.StandardError.ReadToEndAsync(cancellation);
                        await p.WaitForExitAsync(cancellation);
                        var stderr = await stderrTask;
                        if (p.ExitCode == 0)
                        {
                            _logger.LogInformation("✅ {ScriptName} scraper completed successfully in cycle", scriptName);
                        }
                        else
                        {
                            _logger.LogWarning("⚠️ {ScriptName} scraper exited with code {Code}. Stderr: {Error}", scriptName, p.ExitCode, stderr?.Trim());
                        }
                    }
                }
                else
                {
                    _logger.LogDebug("Script {ScriptName} not found in searched locations; skipping Python execution.", scriptName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{ScriptName} scraper execution in cycle encountered a non-fatal exception", scriptName);
            }
        }

        await Upsert("tenders", external, cancellation);
        if (rows.Count + external.Count == 0) throw new InvalidOperationException("No tenders returned by the sources; sync cannot be confirmed");

        var plans = await scraper.ScrapeMultipleAnnualProcurementPlanPagesAsync(1, pagesToScrape).WaitAsync(cancellation);
        var planRows = new List<JsonObject>();
        foreach (var plan in plans)
        {
            var payload = JsonSerializer.SerializeToNode(plan, JsonOptions)!.AsObject();
            var id = "praz:" + StableId((payload["procuringEntity"]?.ToString() ?? "") + "|" + payload["year"]);
            payload["id"] = id;
            planRows.Add(new JsonObject { ["id"] = id, ["country"] = "ZW", ["payload"] = payload, ["scraped_at"] = DateTimeOffset.UtcNow });
        }
        await Upsert("procurement_plans", planRows, cancellation);

        // Enrich a bounded set each cycle, preserving prior details on failure.
        var enrichLimit = Math.Clamp(_config.GetValue("Scraper:EnrichLimit", 20), 1, 100);
        var pending = await _client.GetFromJsonAsync<JsonArray>($"rest/v1/tenders?source=eq.praz&status=eq.live&details=is.null&select=id,source_id&limit={enrichLimit}", cancellation);
        foreach (var item in pending ?? new JsonArray())
        {
            try
            {
                var detail = await scraper.GetZimbabweTenderDetailsAsync(item!["source_id"]!.GetValue<string>()).WaitAsync(cancellation);
                using var response = await _client.PatchAsJsonAsync("rest/v1/tenders?id=eq." + Uri.EscapeDataString(item!["id"]!.GetValue<string>()), new { details = detail }, cancellation);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Could not enrich tender details; will retry"); }
        }
        var pendingPlans = await _client.GetFromJsonAsync<JsonArray>($"rest/v1/procurement_plans?details=is.null&select=id,payload&limit={enrichLimit}", cancellation);
        foreach (var item in pendingPlans ?? new JsonArray())
        {
            var url = item?["payload"]?["viewAppUrl"]?.GetValue<string>();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "egp.praz.org.zw") continue;
            try
            {
                var detail = await scraper.GetAnnualProcurementPlanDetailAsync(url!).WaitAsync(cancellation);
                using var response = await _client.PatchAsJsonAsync("rest/v1/procurement_plans?id=eq." + Uri.EscapeDataString(item!["id"]!.GetValue<string>()), new { details = detail }, cancellation);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Could not enrich procurement plan; will retry"); }
        }
        _logger.LogInformation("Published {Count} tender records and {Plans} procurement plans to Supabase", rows.Count + external.Count, plans.Count);
        return rows.Count + external.Count;
    }
}
