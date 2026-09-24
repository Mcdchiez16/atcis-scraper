using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace ZimbabweTenderAPI.Services;

/// <summary>
/// Deterministic tender subject classification used before every Supabase upsert.
/// Only the procurement subject is considered; buyer names and old category labels
/// are deliberately excluded because they are not reliable evidence.
/// </summary>
public static partial class TenderClassificationRules
{
    public const string Version = "tender-rules-v2";

    private sealed record SectorRule(string Category, string[] Keywords, string[] TitleIntent);
    public sealed record MatchResult(string Sector, string Category, int Confidence, string[] Evidence, bool NeedsReview);

    private static readonly IReadOnlyDictionary<string, SectorRule> Rules =
        new Dictionary<string, SectorRule>(StringComparer.Ordinal)
        {
            ["ICT & Software"] = new(
                "ICT & Software Solutions",
                ["ict", "information technology", "software", "software license", "computer", "laptop", "server", "network", "networking", "database", "cybersecurity", "telecommunication", "telecom", "cloud computing", "erp system", "website development", "mobile application", "firewall", "data centre", "data center", "computer hardware", "printer", "photocopier", "structured cabling", "digital platform", "information system"],
                []),
            ["Healthcare & Medical"] = new(
                "Healthcare & Medical Supplies",
                ["medical equipment", "medical supplies", "medical consumables", "healthcare equipment", "healthcare supplies", "pharmaceutical", "medicine", "medicines", "surgical", "surgical supplies", "hospital supplies", "hospital equipment", "clinical supplies", "diagnostic", "laboratory reagent", "laboratory equipment", "vaccine", "patient monitor", "medical device", "dialysis", "radiology", "x ray", "ultrasound", "ambulance", "blood pressure monitor", "oxygen concentrator", "medical gas", "syringe", "catheter", "dental equipment", "test kit"],
                []),
            ["Electrical & Energy"] = new(
                "Electrical, Energy & Utilities",
                ["electrical works", "electrical equipment", "electricity", "solar", "solar panel", "renewable energy", "power supply", "power generation", "power distribution", "transmission line", "electrical grid", "mini grid", "transformer", "substation", "generator", "switchgear", "inverter", "photovoltaic", "high voltage", "circuit breaker", "smart meter", "power transmission", "electromechanical equipment"],
                []),
            ["Civil & Infrastructure"] = new(
                "Construction & Civil Infrastructure",
                ["civil works", "construction works", "construction", "road works", "road construction", "bridge construction", "building works", "building construction", "rehabilitation works", "renovation works", "refurbishment works", "water infrastructure", "water supply works", "sewer works", "sanitation works", "borehole drilling", "drainage works", "earthworks", "roofing works", "paving works", "concrete works", "fencing works", "irrigation works", "consolidation works"],
                ["construction", "civil works", "road works", "building works", "rehabilitation works", "renovation works"]),
            ["General Goods & Consumables"] = new(
                "General Goods, Agriculture & Supplies",
                ["general goods", "general supplies", "office supplies", "stationery", "office furniture", "school furniture", "household furniture", "home economics equipment", "kitchen equipment", "cleaning materials", "cleaning supplies", "detergent", "uniform", "protective clothing", "personal protective equipment", "industrial ppe", "food supplies", "food products", "groceries", "agricultural inputs", "farming inputs", "fertilizer", "seed supply", "livestock", "animal feed", "printing supplies", "building materials", "tools and hardware", "building hardware", "furniture", "banner", "printing services", "air conditioner", "pipes and fittings", "weather equipment", "forensic laboratory", "laboratory furniture", "laboratory cupboards", "fuel supply", "lubricant supply"],
                []),
            ["Services & Logistics"] = new(
                "Professional, Transport & Logistics Services",
                ["consultancy", "consulting services", "professional services", "advisory services", "audit services", "external audit", "training services", "research services", "feasibility study", "technical assistance", "monitoring and evaluation", "project supervision", "design services", "transport services", "logistics services", "freight forwarding", "courier services", "vehicle hire", "car hire", "fleet management", "vehicle maintenance", "security services", "guarding services", "cleaning services", "catering services", "insurance services", "legal services", "recruitment services", "consultant", "advisor", "advisory", "evaluation", "assessment", "review", "survey", "capacity building", "editing", "translation", "resource mobilization", "vehicle", "minibus", "truck", "automotive"],
                ["consultancy", "consulting services", "audit services", "external audit", "feasibility study", "technical assistance", "training services", "consultant", "advisor", "evaluation", "assessment", "review", "survey"]),
        };

    public static void Apply(JsonObject payload)
    {
        var result = Classify(
            payload["title"]?.ToString(),
            payload["description"]?.ToString(),
            payload["scope"]?.ToString(),
            ExtractLineItems(payload["lineItems"]));

        payload["classificationVersion"] = Version;
        payload["classificationSource"] = "rules";
        payload["sector"] = result.Sector;
        payload["category"] = result.Category;
        payload["categoryNames"] = new JsonArray(JsonValue.Create(result.Category));
        payload["aiScore"] = result.Confidence;
        payload["classificationConfidence"] = result.Confidence;
        payload["classificationEvidence"] = new JsonArray(
            result.Evidence.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray());
        payload["needsClassificationReview"] = result.NeedsReview;
    }

    public static MatchResult Classify(string? title, string? description = null, string? scope = null, string? lineItems = null)
    {
        var fields = new[]
        {
            (Name: "title", Text: Normalize(title), Weight: 12d),
            (Name: "scope", Text: Normalize(scope), Weight: 6d),
            (Name: "line items", Text: Normalize(lineItems), Weight: 5d),
            (Name: "description", Text: Normalize(description), Weight: 2d),
        };
        var scores = Rules.Keys.ToDictionary(key => key, _ => 0d, StringComparer.Ordinal);
        var evidence = Rules.Keys.ToDictionary(key => key, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var (sector, rule) in Rules)
        {
            foreach (var keyword in rule.Keywords)
            {
                foreach (var field in fields)
                {
                    if (field.Text.Length == 0 || !ContainsPhrase(field.Text, keyword)) continue;
                    var wordCount = Normalize(keyword).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                    var specificity = 1d + Math.Min(0.5d, Math.Max(0, wordCount - 1) * 0.15d);
                    scores[sector] += field.Weight * specificity;
                    evidence[sector].Add($"{field.Name}: {keyword}");
                }
            }
        }

        foreach (var (sector, rule) in Rules)
        {
            if (rule.TitleIntent.Any(keyword => ContainsPhrase(fields[0].Text, keyword))) scores[sector] += 12;
        }

        var ranked = scores.OrderByDescending(item => item.Value).ThenBy(item => item.Key, StringComparer.Ordinal).ToArray();
        var (topSector, topScore) = ranked[0];
        var margin = topScore - ranked[1].Value;
        var hasTitleEvidence = evidence[topSector].Any(item => item.StartsWith("title:", StringComparison.Ordinal));
        var conclusive = topScore >= 10 && hasTitleEvidence && margin >= 4;

        if (topScore == 0)
            return new("Other", "Needs Classification Review", 25, [], true);

        var confidence = (int)Math.Round(
            Math.Min(98, 60 + Math.Min(24, topScore * 1.1) + Math.Min(14, Math.Max(0, margin))),
            MidpointRounding.ToEven);
        if (!conclusive) confidence = Math.Min(confidence, 69);

        return new(
            topSector,
            Rules[topSector].Category,
            confidence,
            evidence[topSector].Distinct(StringComparer.Ordinal).Take(5).ToArray(),
            !conclusive);
    }

    private static string ExtractLineItems(JsonNode? node)
    {
        if (node is not JsonArray items) return string.Empty;
        return string.Join(' ', items.Select(item => item is JsonObject obj
            ? $"{obj["description"]} {obj["specification"]}"
            : item?.ToString()));
    }

    private static bool ContainsPhrase(string normalizedText, string phrase)
    {
        var normalizedPhrase = Normalize(phrase);
        return normalizedPhrase.Length > 0 && Regex.IsMatch(
            normalizedText,
            $@"(?<![a-z0-9]){Regex.Escape(normalizedPhrase)}(?![a-z0-9])",
            RegexOptions.CultureInvariant);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var decomposed = value.Normalize(NormalizationForm.FormD).ToLowerInvariant();
        var builder = new StringBuilder(decomposed.Length);
        var previousWasSpace = true;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(character);
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }
        return builder.ToString().Trim();
    }
}
