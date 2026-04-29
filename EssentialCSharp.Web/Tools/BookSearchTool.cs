using System.ComponentModel;
using System.Text.RegularExpressions;
using EssentialCSharp.Chat.Common.Services;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EssentialCSharp.Web.Tools;

[McpServerToolType]
public sealed class BookSearchTool
{
    private readonly AISearchService? _SearchService;
    private readonly ISiteMappingService _SiteMappingService;
    private readonly IGuidelinesService _guidelinesService;
    private readonly IBookToolQueryService _bookToolQueryService;

    public BookSearchTool(
        IServiceProvider serviceProvider,
        ISiteMappingService siteMappingService,
        IGuidelinesService guidelinesService,
        IBookToolQueryService bookToolQueryService)
    {
        _SearchService = serviceProvider.GetService<AISearchService>();
        _SiteMappingService = siteMappingService;
        _guidelinesService = guidelinesService;
        _bookToolQueryService = bookToolQueryService;
    }

    [McpServerTool(Title = "Search Book Content", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Search the Essential C# book content using semantic vector search. Returns relevant text chunks with chapter and heading context. Use this to find information about C# programming concepts covered in the book.")]
    public async Task<string> SearchBookContent(
        [Description("The search query describing the C# concept or topic to find in the book.")] string query,
        [Description("Number of results to return (1–10). Use a higher value for broad topics or comprehensive research; lower for quick lookups.")] int maxResults = AISearchService.DefaultSearchTop,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return "Query must not be empty.";
        }
        if (query.Length > 500)
        {
            return "Query is too long (maximum 500 characters).";
        }

        if (_SearchService is null)
        {
            return "Book search is not available in this environment (AI services are not configured).";
        }

        List<SearchBookContentMatchTextResult> matches = (await _SearchService.ExecuteVectorSearch(
                query,
                top: maxResults,
                cancellationToken: cancellationToken))
            .Select(result => new SearchBookContentMatchTextResult(
                result.Score ?? 0,
                result.Record.ChapterNumber,
                result.Record.Heading,
                result.Record.ChunkText))
            .ToList();

        if (matches.Count == 0)
        {
            return "No results found for the given query.";
        }

        return new SearchBookContentTextResult(matches).ToMcpString();
    }

    [McpServerTool(Title = "Get Chapter List", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
     Description("Get the table of contents for the Essential C# book, listing all chapters and their sections with navigation links.")]
    public ChapterListToolResult GetChapterList() => _bookToolQueryService.GetChapterList();

    [McpServerTool(Title = "Get Chapter Sections", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
     Description("Get all sections and subsections in a specific chapter of the Essential C# book, in reading order. Returns each section's heading, slug, anchor link, and indent level. Use the returned slugs with other tools like GetSectionContent or GetNavigationContext.")]
    public ChapterSectionsToolResult GetChapterSections(
        [Description("The chapter number (e.g., 5 for Chapter 5).")] int chapter) =>
        _bookToolQueryService.GetChapterSections(chapter);

    [McpServerTool(Title = "Get Direct Content URL", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
     Description("Get the canonical deep-link URL and section metadata for a specific book section or subsection. Use this to include precise references in responses.")]
    public BookSectionReferenceResult GetDirectContentUrl(
        [Description("The section slug/key (e.g., 'hello-world'). Use GetChapterSections or GetChapterList to find valid slugs.")] string sectionKey) =>
        _bookToolQueryService.GetDirectContentUrl(sectionKey);

    [McpServerTool(Title = "Lookup Concept", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Find all sections in the Essential C# book that cover a specific C# concept. Combines section heading search with semantic vector search (when available) to give broad coverage. Returns section slugs, chapter numbers, and direct links.")]
    public async Task<string> LookupConcept(
        [Description("The C# concept, feature, or topic to find in the book (e.g., 'LINQ', 'async/await', 'pattern matching', 'generics').")] string concept,
        [Description("Number of semantic search results to return (1–10).")] int maxResults = AISearchService.DefaultSearchTop,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(concept))
        {
            return "Concept must not be empty.";
        }
        if (concept.Length > 500)
        {
            return "Concept is too long (maximum 500 characters).";
        }

        // Heading / key text search
        var headingMatches = _SiteMappingService.SiteMappings
            .Where(m =>
                m.RawHeading.Contains(concept, StringComparison.OrdinalIgnoreCase) ||
                m.Keys.Any(k => k.Contains(concept.Replace(' ', '-'), StringComparison.OrdinalIgnoreCase)))
            .OrderBy(m => m.ChapterNumber)
            .ThenBy(m => m.PageNumber)
            .ThenBy(m => m.OrderOnPage)
            .ToList();

        List<SemanticBookContentMatchTextResult> semanticMatches = [];
        if (_SearchService is not null)
        {
            var results = await _SearchService.ExecuteVectorSearch(concept, top: maxResults, cancellationToken: cancellationToken);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in results)
            {
                string heading = r.Record.Heading ?? "";
                if (!seen.Add(heading))
                {
                    continue;
                }

                semanticMatches.Add(new SemanticBookContentMatchTextResult(
                    r.Record.ChapterNumber ?? 0,
                    heading,
                    r.Record.ChunkText[..Math.Min(200, r.Record.ChunkText.Length)]));
            }
        }

        if (headingMatches.Count == 0 && semanticMatches.Count == 0)
        {
            return $"No book content found for '{concept}'. Try a different term or check the table of contents with GetChapterList.";
        }

        return new LookupConceptTextResult(
            concept,
            headingMatches.Select(ToSectionLink).ToList(),
            semanticMatches).ToMcpString();
    }

    [McpServerTool(Title = "Check Topic Coverage", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Determine whether and how thoroughly the Essential C# book covers a given topic. Returns a coverage assessment: 'Comprehensive', 'Mentioned', or 'Not found in headings'. Use this before citing the book to calibrate confidence.")]
    public async Task<string> CheckTopicCoverage(
        [Description("The C# topic, feature, or concept to check (e.g., 'source generators', 'records', 'LINQ').")] string topic,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return "Topic must not be empty.";
        }
        if (topic.Length > 500)
        {
            return "Topic is too long (maximum 500 characters).";
        }

        // Heading search
        var headingMatches = _SiteMappingService.SiteMappings
            .Where(m =>
                m.RawHeading.Contains(topic, StringComparison.OrdinalIgnoreCase) ||
                m.Keys.Any(k => k.Contains(topic.Replace(' ', '-'), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        bool hasHeadingCoverage = headingMatches.Count > 0;
        bool hasSemanticCoverage = false;
        bool semanticAvailable = _SearchService is not null;

        if (semanticAvailable)
        {
            var results = await _SearchService!.ExecuteVectorSearch(topic, cancellationToken: cancellationToken);
            hasSemanticCoverage = results.Count > 0;
        }

        string assessment;
        if (hasHeadingCoverage)
        {
            assessment = "**Comprehensive** — dedicated section headings found";
        }
        else if (hasSemanticCoverage)
        {
            assessment = "**Mentioned** — referenced in book content but no dedicated section heading";
        }
        else
        {
            assessment = semanticAvailable
                ? "**Not covered** — not found in section headings or semantic search"
                : "**Not found in headings** — semantic search unavailable; topic may still be discussed in prose";
        }

        return new TopicCoverageTextResult(
            topic,
            assessment,
            headingMatches.Take(5).Select(ToSectionLink).ToList()).ToMcpString();
    }

    [McpServerTool(
        Title = "Find Book Help For Diagnostic",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(DiagnosticHelpToolResult)),
     Description("Find Essential C# book sections, content, and coding guidelines that help explain a C# compiler error, warning, or runtime exception. Accepts a CS diagnostic code (e.g., 'CS8600') or a plain description (e.g., 'null reference exception', 'cannot implicitly convert'). Returns relevant sections, explanatory prose, and related guidelines.")]
    public async Task<CallToolResult> FindBookHelpForDiagnostic(
        [Description("A C# compiler diagnostic code (e.g., 'CS8600', 'CS0029') or a plain error description (e.g., 'null reference exception', 'async method lacks await').")] string diagnostic,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return McpToolResultFactory.CreateError("Diagnostic must not be empty.");
        }

        string trimmedDiagnostic = diagnostic.Trim();
        if (trimmedDiagnostic.Length > 500)
        {
            return McpToolResultFactory.CreateError("Diagnostic is too long (maximum 500 characters).");
        }

        string searchTerm = MapDiagnosticToTopic(trimmedDiagnostic);

        // Heading search
        var headingMatches = _SiteMappingService.SiteMappings
            .Where(m => m.RawHeading.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();

        List<BookContentExcerptResult> contentMatches = [];
        if (_SearchService is not null)
        {
            var vectorResults = await _SearchService.ExecuteVectorSearch(searchTerm, cancellationToken: cancellationToken);
            foreach (var r in vectorResults.Take(3))
            {
                contentMatches.Add(new BookContentExcerptResult(
                    r.Record.ChapterNumber,
                    r.Record.Heading,
                    r.Record.ChunkText));
            }
        }

        // Guidelines search
        var guidelineMatches = _guidelinesService.Guidelines
            .Where(g => g.Guideline.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                     || g.SanitizedSubsection.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        List<BookSectionReferenceResult> relevantSections = headingMatches.Select(ToSectionReference).ToList();
        List<BookGuidelineSummaryResult> relatedGuidelines = guidelineMatches.Select(ToGuidelineSummary).ToList();
        DiagnosticHelpToolResult structuredResult = new(
            trimmedDiagnostic,
            string.Equals(searchTerm, trimmedDiagnostic, StringComparison.OrdinalIgnoreCase) ? null : searchTerm,
            relevantSections,
            contentMatches,
            relatedGuidelines,
            _SearchService is not null);

        if (headingMatches.Count == 0 && contentMatches.Count == 0 && guidelineMatches.Count == 0)
        {
            string semanticNote = _SearchService is null
                ? " Semantic search is also unavailable in this environment."
                : "";

            return McpToolResultFactory.CreateHybridResult(
                $"No book content or guidelines found for '{trimmedDiagnostic}'.{semanticNote} Try a broader description or use GetChapterList to explore the table of contents.",
                structuredResult);
        }

        return McpToolResultFactory.CreateHybridResult(
            new DiagnosticHelpTextResult(
                structuredResult.Diagnostic,
                structuredResult.SearchTerm,
                relevantSections.Select(ToTextResult).ToList(),
                contentMatches.Select(ToTextResult).ToList(),
                relatedGuidelines.Select(ToTextResult).ToList()).ToMcpString(),
            structuredResult);
    }

    [McpServerTool(Title = "Find Related Sections", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Find other sections in the Essential C# book that are semantically related to a given section. Uses the section heading as a search query to discover thematically connected content across the entire book. Requires AI services to be configured.")]
    public async Task<string> FindRelatedSections(
        [Description("The section slug/key to find related content for (e.g., 'async-await'). Use GetChapterSections to get valid slugs.")] string sectionKey,
        [Description("Number of related sections to return (1–10).")] int maxResults = AISearchService.DefaultSearchTop,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return "Section key must not be empty. Use GetChapterSections to discover valid section slugs.";
        }

        SiteMapping? mapping = _SiteMappingService.SiteMappings.Find(sectionKey);
        if (mapping is null)
        {
            return $"Section '{sectionKey}' not found. Use GetChapterSections to discover valid section slugs.";
        }

        if (_SearchService is null)
        {
            return "FindRelatedSections requires AI services, which are not configured in this environment. Use LookupConcept for heading-based search.";
        }

        string query = $"{mapping.RawHeading} {mapping.ChapterTitle}";
        var results = await _SearchService.ExecuteVectorSearch(query, top: maxResults, cancellationToken: cancellationToken);

        List<RelatedSectionMatchTextResult> relatedSections = [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mapping.RawHeading };
        foreach (var r in results)
        {
            string heading = r.Record.Heading ?? "";
            if (!seen.Add(heading)) continue;
            if (relatedSections.Count >= maxResults) break;

            // Find the SiteMapping for this heading to get the link
            SiteMapping? relatedMapping = _SiteMappingService.SiteMappings
                .FirstOrDefault(m => m.RawHeading.Equals(heading, StringComparison.OrdinalIgnoreCase)
                                  && m.ChapterNumber == (r.Record.ChapterNumber ?? 0));

            string link = relatedMapping is not null
                ? $"`/{relatedMapping.Keys.FirstOrDefault() ?? relatedMapping.PrimaryKey}#{relatedMapping.AnchorId}`"
                : $"Ch. {r.Record.ChapterNumber}";

            relatedSections.Add(new RelatedSectionMatchTextResult(
                heading,
                link,
                r.Record.ChunkText[..Math.Min(200, r.Record.ChunkText.Length)]));
        }

        return new RelatedSectionsTextResult(mapping.RawHeading, mapping.ChapterNumber, mapping.ChapterTitle, relatedSections)
            .ToMcpString();
    }

    private BookSectionReferenceResult ToSectionReference(SiteMapping mapping) =>
        _bookToolQueryService.GetDirectContentUrl(mapping.Keys.FirstOrDefault() ?? mapping.PrimaryKey);

    private BookSectionLinkTextResult ToSectionLink(SiteMapping mapping) => ToTextResult(ToSectionReference(mapping));

    private static BookSectionLinkTextResult ToTextResult(BookSectionReferenceResult section) =>
        new(section.Heading, section.ChapterNumber, section.Href);

    private static DiagnosticBookContentMatchTextResult ToTextResult(BookContentExcerptResult match) =>
        new(match.ChapterNumber, match.Heading, match.ChunkText);

    private static TextGuidelineResult ToTextResult(BookGuidelineSummaryResult guideline) =>
        new(guideline.Type, guideline.Guideline, guideline.ChapterNumber, guideline.ChapterTitle, guideline.Subsection);

    private static BookGuidelineSummaryResult ToGuidelineSummary(GuidelineListing guideline) =>
        new(
            guideline.Type.ToDisplayString(),
            guideline.Guideline,
            guideline.ChapterNumber,
            guideline.ChapterTitle ?? string.Empty,
            guideline.SanitizedSubsection);

    private static readonly Dictionary<string, string> DiagnosticMap= new(StringComparer.OrdinalIgnoreCase)
    {
        // Nullable reference types
        ["CS8600"] = "nullable reference types",
        ["CS8601"] = "nullable reference types",
        ["CS8602"] = "nullable reference types dereference",
        ["CS8603"] = "nullable reference types",
        ["CS8604"] = "nullable reference types",
        ["CS8618"] = "nullable reference types constructor",
        ["CS8625"] = "nullable reference types null literal",
        // Type conversions
        ["CS0029"] = "implicit type conversion",
        ["CS0030"] = "explicit type casting",
        // Async
        ["CS1998"] = "async await",
        ["CS4014"] = "async await task",
        // Access modifiers
        ["CS0122"] = "access modifiers",
        // Missing members
        ["CS0103"] = "variable declaration scope",
        ["CS0246"] = "using directives namespaces",
        // Interface implementation
        ["CS0535"] = "interface implementation",
        ["CS0738"] = "interface implementation",
        // Override
        ["CS0115"] = "virtual override polymorphism",
        // Generics
        ["CS0314"] = "generics constraints",
        ["CS0453"] = "generics value types",
    };

    private static string MapDiagnosticToTopic(string diagnostic)
    {
        // Try exact CS code match first
        var codeMatch = Regex.Match(diagnostic, @"CS\d+", RegexOptions.IgnoreCase);
        if (codeMatch.Success && DiagnosticMap.TryGetValue(codeMatch.Value.ToUpperInvariant(), out string? mapped))
        {
            return mapped;
        }

        // Fall back to the raw description for vector search
        return diagnostic;
    }
}
