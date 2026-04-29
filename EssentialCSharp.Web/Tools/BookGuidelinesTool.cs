using System.ComponentModel;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Models;
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
     Description("Retrieve C# coding guidelines from the Essential C# book. Filter by keyword (case-insensitive substring match), chapter number, or guideline type. Use the 'topic' parameter for relevance-ranked discovery by concept (e.g., 'exception handling', 'naming', 'async'). Each guideline includes its chapter and subsection context. Tip: use 'topic' for broad discovery; use 'keyword' for precise substring matching.")]
    public string GetCSharpGuidelines(
        [Description("Optional keyword for case-insensitive substring search in guideline text and subsection name.")] string? keyword = null,
        [Description("Optional chapter number to restrict results to a specific chapter.")] int? chapter = null,
        [Description("Optional guideline type: 'do', 'consider', 'avoid', or 'donot' (also accepts 'do not', 'dont').")] string? type = null,
        [Description("Optional topic or concept for relevance-ranked search (e.g., 'exception handling', 'naming', 'async'). Results are ordered by relevance. Use for broad discovery; use 'keyword' for substring text matching.")] string? topic = null,
        [Description("Maximum number of guidelines to return (1–50).")] int maxResults = 20)
    {
        maxResults = Math.Clamp(maxResults, 1, 50);
        GuidelineType? typeFilter = ParseGuidelineType(type);

        if (!string.IsNullOrWhiteSpace(type) && typeFilter is null)
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

        if (!string.IsNullOrWhiteSpace(topic))
        {
            var words = topic.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var scored = filtered
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

            List<TextGuidelineResult> topicResults = scored
                .Select(x => new TextGuidelineResult(
                    x.guideline.Type.ToDisplayString(),
                    x.guideline.Guideline,
                    x.guideline.ChapterNumber,
                    x.guideline.ChapterTitle ?? string.Empty,
                    x.guideline.SanitizedSubsection))
                .ToList();

            return new GuidelinesTextResult(topic, topicResults).ToMcpString();
        }

        var results = filtered.Take(maxResults).ToList();

        if (results.Count == 0)
        {
            return "No guidelines found matching the specified filters.";
        }

        List<TextGuidelineResult> guidelineResults = results
            .Select(g => new TextGuidelineResult(
                g.Type.ToDisplayString(),
                g.Guideline,
                g.ChapterNumber,
                g.ChapterTitle ?? string.Empty,
                g.SanitizedSubsection))
            .ToList();

        return new GuidelinesTextResult(null, guidelineResults).ToMcpString();
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
}
