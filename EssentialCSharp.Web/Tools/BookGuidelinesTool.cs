using System.ComponentModel;
using System.Globalization;
using System.Text;
using EssentialCSharp.Web.Services;
using ModelContextProtocol.Server;

namespace EssentialCSharp.Web.Tools;

[McpServerToolType]
public sealed class BookGuidelinesTool
{
    private readonly IGuidelinesService _guidelinesService;

    public BookGuidelinesTool(IGuidelinesService guidelinesService)
    {
        _guidelinesService = guidelinesService;
    }

    [McpServerTool(Title = "Get C# Guidelines", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Retrieve C# coding guidelines from the Essential C# book. Optionally filter by keyword, chapter number, or guideline type (do/consider/avoid/donot). The book contains guidelines covering naming conventions, error handling, LINQ, async/await, generics, and many other topics. Each guideline includes its chapter and subsection context.")]
    public string GetCSharpGuidelines(
        [Description("Optional keyword to filter guidelines by (searched in guideline text and subsection name).")] string? keyword = null,
        [Description("Optional chapter number to restrict results to a specific chapter.")] int? chapter = null,
        [Description("Optional guideline type: 'do', 'consider', 'avoid', or 'donot' (also accepts 'do not', 'dont').")] string? type = null,
        [Description("Maximum number of guidelines to return (1–50, default 20).")] int maxResults = 20)
    {
        maxResults = Math.Clamp(maxResults, 1, 50);
        GuidelineType? typeFilter = ParseGuidelineType(type);

        if (type is not null && typeFilter is null)
        {
            return "Invalid guideline type. Valid values: 'do', 'consider', 'avoid', 'donot' (also accepts 'do not', 'dont').";
        }

        IEnumerable<GuidelineListing> filtered = _guidelinesService.Guidelines;

        if (chapter is int chapterValue)
            filtered = filtered.Where(g => g.ChapterNumber == chapterValue);

        if (typeFilter is GuidelineType typeFilterValue)
            filtered = filtered.Where(g => g.Type == typeFilterValue);

        if (!string.IsNullOrWhiteSpace(keyword))
            filtered = filtered.Where(g =>
                g.Guideline.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                g.SanitizedSubsection.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                (g.ActualSubsection?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true));

        var results = filtered.Take(maxResults).ToList();

        if (results.Count == 0)
        {
            return "No guidelines found matching the specified filters.";
        }

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Essential C# Guidelines ({results.Count} result{(results.Count == 1 ? "" : "s")})");
        sb.AppendLine();

        foreach (var g in results)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**[{FormatType(g.Type)}]** {g.Guideline}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  — Chapter {g.ChapterNumber}: {g.ChapterTitle} / {g.SanitizedSubsection}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerTool(Title = "Get Guidelines By Topic", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Search C# coding guidelines from the Essential C# book by topic or concept. More discoverable than filtering by chapter — finds all guidelines related to exceptions, naming, async, LINQ, generics, interfaces, and more. Results are ordered by relevance to the topic.")]
    public string GetGuidelinesByTopic(
        [Description("The topic or concept to search guidelines for (e.g., 'exception handling', 'naming', 'async', 'LINQ', 'generics', 'interface').")] string topic,
        [Description("Maximum number of guidelines to return (1–30, default 15).")] int maxResults = 15)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return "Topic must not be empty.";
        }

        maxResults = Math.Clamp(maxResults, 1, 30);

        var words = topic.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var scored = _guidelinesService.Guidelines
            .Select(g =>
            {
                string combined = $"{g.Guideline} {g.SanitizedSubsection} {g.ActualSubsection} {g.ChapterTitle}";
                int score = words.Count(w => combined.Contains(w, StringComparison.OrdinalIgnoreCase));
                return (guideline: g, score);
            })
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(maxResults)
            .ToList();

        if (scored.Count == 0)
        {
            return $"No guidelines found related to '{topic}'.";
        }

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Essential C# Guidelines — Topic: {topic} ({scored.Count} result{(scored.Count == 1 ? "" : "s")})");
        sb.AppendLine();

        foreach (var (g, _) in scored)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**[{FormatType(g.Type)}]** {g.Guideline}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  — Chapter {g.ChapterNumber}: {g.ChapterTitle} / {g.SanitizedSubsection}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static GuidelineType? ParseGuidelineType(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        return input.Trim().ToLowerInvariant().Replace(" ", "").Replace("_", "").Replace("'", "") switch
        {
            "do" => GuidelineType.Do,
            "consider" => GuidelineType.Consider,
            "avoid" => GuidelineType.Avoid,
            "donot" or "dont" or "donotdo" => GuidelineType.DoNot,
            _ => null
        };
    }

    private static string FormatType(GuidelineType type) => type switch
    {
        GuidelineType.Do => "DO",
        GuidelineType.Consider => "CONSIDER",
        GuidelineType.Avoid => "AVOID",
        GuidelineType.DoNot => "DO NOT",
        _ => "NOTE"
    };
}
