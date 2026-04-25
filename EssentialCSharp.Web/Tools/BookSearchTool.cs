using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EssentialCSharp.Chat.Common.Services;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Services;
using ModelContextProtocol.Server;

namespace EssentialCSharp.Web.Tools;

// Book metadata constants — update here when edition changes.
file static class BookMetadata
{
    public const string Title = "Essential C#";
    public const string Edition = "9th Edition";
    public const string CSharpVersion = "C# 13.0";
    public const string Authors = "Mark and Benjamin Michaelis";
    public const string Publisher = "Addison-Wesley Professional";
    public const string Isbn13 = "978-0-13-576056-5";
    public const string SiteUrl = "https://essentialcsharp.com";
}

[McpServerToolType]
public sealed class BookSearchTool
{
    private readonly AISearchService? _SearchService;
    private readonly ISiteMappingService _SiteMappingService;
    private readonly IGuidelinesService _guidelinesService;

    public BookSearchTool(IServiceProvider serviceProvider, ISiteMappingService siteMappingService, IGuidelinesService guidelinesService)
    {
        _SearchService = serviceProvider.GetService<AISearchService>();
        _SiteMappingService = siteMappingService;
        _guidelinesService = guidelinesService;
    }

    [McpServerTool(Title = "Search Book Content", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Search the Essential C# book content using semantic vector search. Returns relevant text chunks with chapter and heading context. Use this to find information about C# programming concepts covered in the book.")]
    public async Task<string> SearchBookContent(
        [Description("The search query describing the C# concept or topic to find in the book.")] string query,
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

        var results = await _SearchService.ExecuteVectorSearch(query, cancellationToken: cancellationToken);

        var sb = new StringBuilder();
        int resultCount = 0;

        foreach (var result in results)
        {
            resultCount++;
            sb.AppendLine(CultureInfo.InvariantCulture, $"--- Result {resultCount} (Score: {result.Score:F4}) ---");

            if (result.Record.ChapterNumber.HasValue)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"Chapter: {result.Record.ChapterNumber}");
            }
            if (!string.IsNullOrEmpty(result.Record.Heading))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"Section: {result.Record.Heading}");
            }

            sb.AppendLine();
            sb.AppendLine(result.Record.ChunkText);
            sb.AppendLine();
        }

        if (resultCount == 0)
        {
            return "No results found for the given query.";
        }

        return sb.ToString();
    }

    [McpServerTool(Title = "Get Chapter List", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get the table of contents for the Essential C# book, listing all chapters and their sections with navigation links.")]
    public string GetChapterList()
    {
        var tocData = _SiteMappingService.GetTocData();

        var sb = new StringBuilder();
        sb.AppendLine("# Essential C# - Table of Contents");
        sb.AppendLine();

        foreach (var chapter in tocData)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"## {chapter.Title}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  Link: {chapter.Href}");

            foreach (var section in chapter.Items)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  - {section.Title} ({section.Href})");

                foreach (var subsection in section.Items)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    - {subsection.Title} ({subsection.Href})");
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerTool(Title = "Get Chapter Sections", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get all sections and subsections in a specific chapter of the Essential C# book, in reading order. Returns each section's heading, slug, anchor link, and indent level. Use the returned slugs with other tools like GetSectionContent or GetNavigationContext.")]
    public string GetChapterSections(
        [Description("The chapter number (e.g., 5 for Chapter 5).")] int chapter)
    {
        var sections = _SiteMappingService.SiteMappings
            .Where(m => m.ChapterNumber == chapter)
            .OrderBy(m => m.PageNumber)
            .ThenBy(m => m.OrderOnPage)
            .ToList();

        if (sections.Count == 0)
        {
            return $"Chapter {chapter} not found. Use GetChapterList to see all available chapters.";
        }

        string chapterTitle = sections.First().ChapterTitle;
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Chapter {chapter}: {chapterTitle} — Sections");
        sb.AppendLine();

        foreach (var m in sections)
        {
            string indent = new(' ', m.IndentLevel * 2);
            string slug = m.Keys.FirstOrDefault() ?? m.PrimaryKey;
            string anchor = m.AnchorId is not null ? $"#{m.AnchorId}" : "";
            sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}- {m.RawHeading}  (slug: `{slug}`, link: `/{slug}{anchor}`)");
        }

        return sb.ToString();
    }

    [McpServerTool(Title = "Get Direct Content URL", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get the canonical deep-link URL for a specific book section or subsection. Returns a clickable URL that navigates directly to the section. Use this to include precise references in responses.")]
    public string GetDirectContentUrl(
        [Description("The section slug/key (e.g., 'hello-world'). Use GetChapterSections or GetChapterList to find valid slugs.")] string sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return "Section key must not be empty. Use GetChapterSections or GetChapterList to discover valid section slugs.";
        }

        SiteMapping? mapping = _SiteMappingService.SiteMappings.Find(sectionKey);
        if (mapping is null)
        {
            return $"Section '{sectionKey}' not found. Use GetChapterSections or GetChapterList to find valid section slugs.";
        }

        string slug = mapping.Keys.FirstOrDefault() ?? mapping.PrimaryKey;
        string anchor = mapping.AnchorId is not null ? $"#{mapping.AnchorId}" : "";
        string url = $"{BookMetadata.SiteUrl}/{slug}{anchor}";

        return $"**{mapping.RawHeading}**\n" +
               $"Chapter {mapping.ChapterNumber}: {mapping.ChapterTitle}\n" +
               $"URL: {url}";
    }

    [McpServerTool(Title = "Get Book Metadata", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get citation-quality metadata for the Essential C# book: title, authors, edition, C# version covered, ISBN, publisher, and website URL. Use this when generating citations or when asked which edition or C# version the book covers.")]
    public string GetBookMetadata()
    {
        return $"""
                # {BookMetadata.Title} — Book Metadata

                **Title:** {BookMetadata.Title}
                **Edition:** {BookMetadata.Edition}
                **C# Version:** {BookMetadata.CSharpVersion}
                **Authors:** {BookMetadata.Authors}
                **Publisher:** {BookMetadata.Publisher}
                **ISBN-13:** {BookMetadata.Isbn13}
                **Website:** {BookMetadata.SiteUrl}
                """;
    }

    [McpServerTool(Title = "Lookup Concept", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Find all sections in the Essential C# book that cover a specific C# concept. Combines section heading search with semantic vector search (when available) to give broad coverage. Returns section slugs, chapter numbers, and direct links.")]
    public async Task<string> LookupConcept(
        [Description("The C# concept, feature, or topic to find in the book (e.g., 'LINQ', 'async/await', 'pattern matching', 'generics').")] string concept,
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

        // Vector search results
        var vectorMatches = new List<(int chapter, string heading, string chunkText)>();
        if (_SearchService is not null)
        {
            var results = await _SearchService.ExecuteVectorSearch(concept, cancellationToken: cancellationToken);
            foreach (var r in results)
            {
                vectorMatches.Add((r.Record.ChapterNumber ?? 0, r.Record.Heading ?? "", r.Record.ChunkText));
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Book Coverage: '{concept}'");
        sb.AppendLine();

        if (headingMatches.Count > 0)
        {
            sb.AppendLine("## Sections with matching headings");
            foreach (var m in headingMatches)
            {
                string slug = m.Keys.FirstOrDefault() ?? m.PrimaryKey;
                string anchor = m.AnchorId is not null ? $"#{m.AnchorId}" : "";
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"- **{m.RawHeading}** (Ch. {m.ChapterNumber}) — `/{slug}{anchor}`");
            }
            sb.AppendLine();
        }

        if (vectorMatches.Count > 0)
        {
            sb.AppendLine("## Related content (semantic search)");
            // Deduplicate by heading
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (ch, heading, text) in vectorMatches)
            {
                if (!seen.Add(heading)) continue;
                sb.AppendLine(CultureInfo.InvariantCulture, $"- **{heading}** (Ch. {ch})");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  > {text[..Math.Min(200, text.Length)]}...");
            }
            sb.AppendLine();
        }

        if (headingMatches.Count == 0 && vectorMatches.Count == 0)
        {
            return $"No book content found for '{concept}'. Try a different term or check the table of contents with GetChapterList.";
        }

        return sb.ToString();
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

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Topic Coverage: '{topic}'");
        sb.AppendLine();

        string assessment;
        if (hasHeadingCoverage)
        {
            assessment = "**Comprehensive** — dedicated section headings found";
        }
        else if (hasSemanticCoverage)
        {
            assessment = "**Mentioned** — referenced in book content but no dedicated section heading";
        }
        else if (!semanticAvailable)
        {
            assessment = "**Not found in headings** — semantic search unavailable; topic may still be discussed in prose";
        }
        else
        {
            assessment = "**Not covered** — not found in section headings or semantic search";
        }

        sb.AppendLine(CultureInfo.InvariantCulture, $"**Assessment:** {assessment}");

        if (headingMatches.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**Relevant sections:**");
            foreach (var m in headingMatches.Take(5))
            {
                string slug = m.Keys.FirstOrDefault() ?? m.PrimaryKey;
                sb.AppendLine(CultureInfo.InvariantCulture, $"  - {m.RawHeading} (Ch. {m.ChapterNumber}) — `/{slug}#{m.AnchorId}`");
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Title = "Find Book Help For Diagnostic", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Find Essential C# book sections, content, and coding guidelines that help explain a C# compiler error, warning, or runtime exception. Accepts a CS diagnostic code (e.g., 'CS8600') or a plain description (e.g., 'null reference exception', 'cannot implicitly convert'). Returns relevant sections, explanatory prose, and related guidelines.")]
    public async Task<string> FindBookHelpForDiagnostic(
        [Description("A C# compiler diagnostic code (e.g., 'CS8600', 'CS0029') or a plain error description (e.g., 'null reference exception', 'async method lacks await').")] string diagnostic,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(diagnostic))
        {
            return "Diagnostic must not be empty.";
        }
        if (diagnostic.Length > 500)
        {
            return "Diagnostic is too long (maximum 500 characters).";
        }

        string searchTerm = MapDiagnosticToTopic(diagnostic);

        // Heading search
        var headingMatches = _SiteMappingService.SiteMappings
            .Where(m => m.RawHeading.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .Take(5)
            .ToList();

        // Vector search (buffered so we can check for any results before writing header)
        bool hasVectorResults = false;
        var vectorSb = new StringBuilder();
        if (_SearchService is not null)
        {
            var vectorResults = await _SearchService.ExecuteVectorSearch(searchTerm, cancellationToken: cancellationToken);
            if (vectorResults.Count > 0)
            {
                hasVectorResults = true;
                vectorSb.AppendLine("## Relevant Book Content");
                int count = 0;
                foreach (var r in vectorResults)
                {
                    if (count++ >= 3) break;
                    if (!string.IsNullOrEmpty(r.Record.Heading))
                    {
                        vectorSb.AppendLine(CultureInfo.InvariantCulture, $"**{r.Record.Heading}** (Ch. {r.Record.ChapterNumber})");
                    }
                    vectorSb.AppendLine(r.Record.ChunkText);
                    vectorSb.AppendLine();
                }
            }
        }

        // Guidelines search
        var guidelineMatches = _guidelinesService.Guidelines
            .Where(g => g.Guideline.Contains(searchTerm, StringComparison.OrdinalIgnoreCase)
                     || g.SanitizedSubsection.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .ToList();

        if (headingMatches.Count == 0 && !hasVectorResults && guidelineMatches.Count == 0)
        {
            string semanticNote = _SearchService is null
                ? " Semantic search is also unavailable in this environment."
                : "";
            return $"No book content or guidelines found for '{diagnostic}'.{semanticNote} Try a broader description or use GetChapterList to explore the table of contents.";
        }

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Book Help for: {diagnostic}");
        if (!string.Equals(searchTerm, diagnostic, StringComparison.OrdinalIgnoreCase))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Searching for: '{searchTerm}'");
        }
        sb.AppendLine();

        if (headingMatches.Count > 0)
        {
            sb.AppendLine("## Relevant Book Sections");
            foreach (var m in headingMatches)
            {
                string slug = m.Keys.FirstOrDefault() ?? m.PrimaryKey;
                sb.AppendLine(CultureInfo.InvariantCulture, $"- **{m.RawHeading}** (Ch. {m.ChapterNumber}) — `/{slug}#{m.AnchorId}`");
            }
            sb.AppendLine();
        }

        if (vectorSb.Length > 0)
        {
            sb.Append(vectorSb);
        }

        if (guidelineMatches.Count > 0)
        {
            sb.AppendLine("## Related Guidelines");
            foreach (var g in guidelineMatches)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"**[{FormatGuidelineType(g.Type)}]** {g.Guideline}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  — Chapter {g.ChapterNumber}: {g.ChapterTitle} / {g.SanitizedSubsection}");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Title = "Find Related Sections", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Find other sections in the Essential C# book that are semantically related to a given section. Uses the section heading as a search query to discover thematically connected content across the entire book. Requires AI services to be configured.")]
    public async Task<string> FindRelatedSections(
        [Description("The section slug/key to find related content for (e.g., 'async-await'). Use GetChapterSections to get valid slugs.")] string sectionKey,
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
        var results = await _SearchService.ExecuteVectorSearch(query, cancellationToken: cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Sections Related to: {mapping.RawHeading}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"(Chapter {mapping.ChapterNumber}: {mapping.ChapterTitle})");
        sb.AppendLine();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { mapping.RawHeading };
        int count = 0;
        foreach (var r in results)
        {
            string heading = r.Record.Heading ?? "";
            if (!seen.Add(heading)) continue;
            if (count++ >= 3) break;

            // Find the SiteMapping for this heading to get the link
            SiteMapping? relatedMapping = _SiteMappingService.SiteMappings
                .FirstOrDefault(m => m.RawHeading.Equals(heading, StringComparison.OrdinalIgnoreCase)
                                  && m.ChapterNumber == (r.Record.ChapterNumber ?? 0));

            string link = relatedMapping is not null
                ? $"`/{relatedMapping.Keys.FirstOrDefault() ?? relatedMapping.PrimaryKey}#{relatedMapping.AnchorId}`"
                : $"Ch. {r.Record.ChapterNumber}";

            sb.AppendLine(CultureInfo.InvariantCulture, $"- **{heading}** ({link})");
            sb.AppendLine(CultureInfo.InvariantCulture, $"  > {r.Record.ChunkText[..Math.Min(200, r.Record.ChunkText.Length)]}...");
            sb.AppendLine();
        }

        if (count == 0)
        {
            sb.AppendLine("No related sections found.");
        }

        return sb.ToString();
    }

    private static string FormatGuidelineType(GuidelineType type) => type switch
    {
        GuidelineType.Do => "DO",
        GuidelineType.Consider => "CONSIDER",
        GuidelineType.Avoid => "AVOID",
        GuidelineType.DoNot => "DO NOT",
        _ => "NOTE"
    };

    private static readonly Dictionary<string, string> DiagnosticMap = new(StringComparer.OrdinalIgnoreCase)
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
