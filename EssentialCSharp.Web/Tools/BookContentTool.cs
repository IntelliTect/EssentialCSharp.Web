using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EssentialCSharp.Chat.Common.Services;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Services;
using HtmlAgilityPack;
using ModelContextProtocol.Server;

namespace EssentialCSharp.Web.Tools;

[McpServerToolType]
public sealed partial class BookContentTool
{
    private readonly ISiteMappingService _siteMappingService;
    private readonly IListingSourceCodeService _listingService;
    private readonly IGuidelinesService _guidelinesService;
    private readonly IWebHostEnvironment _environment;
    private readonly AISearchService? _searchService;

    public BookContentTool(
        ISiteMappingService siteMappingService,
        IListingSourceCodeService listingService,
        IGuidelinesService guidelinesService,
        IWebHostEnvironment environment,
        IServiceProvider serviceProvider)
    {
        _siteMappingService = siteMappingService;
        _listingService = listingService;
        _guidelinesService = guidelinesService;
        _environment = environment;
        _searchService = serviceProvider.GetService<AISearchService>();
    }

    [McpServerTool(Title = "Get Section Content", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Retrieve the prose content of a specific book section identified by its slug/key (e.g., 'hello-world', 'creating-editing-compiling-and-running-c-source-code'). Returns the section text with code examples preserved. Use GetChapterSections to discover available slugs.")]
    public async Task<string> GetSectionContent(
        [Description("The section slug/key (e.g., 'hello-world'). Use GetChapterSections to get valid slugs.")] string sectionKey,
        [Description("Maximum number of characters to return (500–8000). Long sections are truncated.")] int maxChars = 4000,
        CancellationToken cancellationToken = default)
    {
        maxChars = Math.Clamp(maxChars, 500, 8000);

        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return "Section key must not be empty. Use GetChapterSections to discover valid section slugs.";
        }

        SiteMapping? mapping = _siteMappingService.SiteMappings.Find(sectionKey);
        if (mapping is null)
        {
            return $"Section '{sectionKey}' not found. Use GetChapterSections to discover valid section slugs.";
        }
        if (mapping.AnchorId is null || string.IsNullOrWhiteSpace(mapping.AnchorId))
        {
            return $"Section '{sectionKey}' does not have an anchor ID and cannot be extracted.";
        }
        if (!AnchorIdRegex().IsMatch(mapping.AnchorId))
        {
            return $"Section '{sectionKey}' has an invalid anchor ID.";
        }

        string contentRoot = Path.GetFullPath(_environment.ContentRootPath);
        string filePath = Path.GetFullPath(Path.Join(contentRoot, Path.Join(mapping.PagePath)));
        string relative = Path.GetRelativePath(contentRoot, filePath);
        if (relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            return $"Section '{sectionKey}' has an invalid path.";
        }
        if (!string.Equals(Path.GetExtension(filePath), ".html", StringComparison.OrdinalIgnoreCase))
        {
            return $"Section '{sectionKey}' has an invalid path.";
        }

        string htmlContent;
        try
        {
            htmlContent = await File.ReadAllTextAsync(filePath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return $"Chapter HTML file not found for section '{sectionKey}'. Content may not be generated yet.";
        }
        catch (DirectoryNotFoundException)
        {
            return $"Chapter HTML file not found for section '{sectionKey}'. Content may not be generated yet.";
        }
        catch (UnauthorizedAccessException)
        {
            return $"Chapter HTML could not be accessed for section '{sectionKey}'.";
        }
        catch (IOException)
        {
            return $"Failed to read chapter HTML for section '{sectionKey}'.";
        }

        HtmlDocument doc = new();
        doc.LoadHtml(htmlContent);

        var sectionNode = doc.DocumentNode.SelectSingleNode(
            $"//div[@id='{mapping.AnchorId}' and contains(@class,'section-heading')]");

        if (sectionNode is null)
        {
            return $"Section heading element not found for anchor '{mapping.AnchorId}'.";
        }

        var parent = sectionNode.ParentNode;
        var header = new StringBuilder();
        header.AppendLine(CultureInfo.InvariantCulture, $"## {mapping.RawHeading}");
        header.AppendLine(CultureInfo.InvariantCulture, $"Chapter {mapping.ChapterNumber}: {mapping.ChapterTitle}");
        header.AppendLine();

        var body = new StringBuilder();
        bool collecting = false;
        foreach (HtmlNode child in parent.ChildNodes)
        {
            if (!collecting)
            {
                if (child == sectionNode) collecting = true;
                continue;
            }

            // Stop at the next section heading div with an id attribute
            if (child.Name == "div" &&
                child.HasAttributes &&
                !string.IsNullOrEmpty(child.GetAttributeValue("id", "")) &&
                child.GetAttributeValue("class", "").Contains("section-heading"))
            {
                break;
            }

            ExtractNodeContent(child, body);

            if (body.Length >= maxChars)
            {
                body.Append("\n\n[Content truncated — use a larger maxChars value to see more.]");
                break;
            }
        }

        return body.Length == 0 ? $"No content found after section heading '{mapping.RawHeading}'." : header.Append(body).ToString();
    }

    [McpServerTool(Title = "Get Listing With Context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Retrieve a specific book listing's source code together with the semantic book content that explains it. Combines code from GetListingSourceCode with related explanatory text found via search. Ideal for understanding what a listing demonstrates.")]
    public async Task<string> GetListingWithContext(
        [Description("The chapter number of the listing.")] int chapter,
        [Description("The listing number (e.g., 3 for Listing 5.3).")] int listing,
        CancellationToken cancellationToken = default)
    {
        var response = await _listingService.GetListingAsync(chapter, listing);
        if (response is null)
        {
            return $"Listing {chapter}.{listing} not found. Verify the chapter and listing numbers.";
        }

        string langHint = response.FileExtension == "cs" ? "csharp" : response.FileExtension;
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Listing {response.ChapterNumber}.{response.ListingNumber}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"```{langHint}");
        sb.AppendLine(response.Content);
        sb.AppendLine("```");
        sb.AppendLine();

        if (_searchService is not null)
        {
            string query = $"Chapter {chapter} listing {listing} {response.Content[..Math.Min(200, response.Content.Length)]}";
            var contextResults = await _searchService.ExecuteVectorSearch(query, cancellationToken: cancellationToken);
            if (contextResults.Count > 0)
            {
                sb.AppendLine("### Related Book Explanations");
                sb.AppendLine();
                int count = 0;
                foreach (var result in contextResults)
                {
                    if (count++ >= 3) break;
                    if (!string.IsNullOrEmpty(result.Record.Heading))
                    {
                        sb.AppendLine(CultureInfo.InvariantCulture, $"**{result.Record.Heading}** (Chapter {result.Record.ChapterNumber})");
                    }
                    sb.AppendLine(result.Record.ChunkText);
                    sb.AppendLine();
                }
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Title = "Get Navigation Context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get the navigation context for a book section: its breadcrumb path, the previous and next sections, its parent section, and its sibling sections. Useful for understanding where a section sits in the book's structure.")]
    public string GetNavigationContext(
        [Description("The section slug/key (e.g., 'hello-world'). Use GetChapterSections to get valid slugs.")] string sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            return "Section key must not be empty. Use GetChapterSections to discover valid section slugs.";
        }

        SiteMapping? mapping = _siteMappingService.SiteMappings.Find(sectionKey);
        if (mapping is null)
        {
            return $"Section '{sectionKey}' not found. Use GetChapterSections to discover valid section slugs.";
        }

        var ordered = _siteMappingService.SiteMappings
            .OrderBy(m => m.ChapterNumber)
            .ThenBy(m => m.PageNumber)
            .ThenBy(m => m.OrderOnPage)
            .ToList();

        int idx = ordered.FindIndex(m => ReferenceEquals(m, mapping));
        if (idx < 0)
        {
            return $"Section '{sectionKey}' could not be located in the ordered mapping list.";
        }

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"## Navigation Context: {mapping.RawHeading}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Chapter {mapping.ChapterNumber}: {mapping.ChapterTitle} | Indent level: {mapping.IndentLevel}");
        sb.AppendLine();

        // Breadcrumb: ancestors (same chapter, descending indent levels)
        var breadcrumb = new List<SiteMapping>();
        int targetIndent = mapping.IndentLevel;
        for (int i = idx - 1; i >= 0 && targetIndent > 0; i--)
        {
            if (ordered[i].ChapterNumber != mapping.ChapterNumber) break;
            if (ordered[i].IndentLevel < targetIndent)
            {
                breadcrumb.Insert(0, ordered[i]);
                targetIndent = ordered[i].IndentLevel;
            }
        }
        if (breadcrumb.Count > 0)
        {
            sb.Append("**Breadcrumb:** ");
            sb.AppendJoin(" > ", breadcrumb.Select(m => m.RawHeading));
            sb.AppendLine(CultureInfo.InvariantCulture, $" > {mapping.RawHeading}");
            sb.AppendLine();
        }

        // Parent: nearest preceding mapping in same chapter with indent level - 1
        SiteMapping? parent = null;
        if (mapping.IndentLevel > 0)
        {
            for (int i = idx - 1; i >= 0; i--)
            {
                if (ordered[i].ChapterNumber != mapping.ChapterNumber) break;
                if (ordered[i].IndentLevel == mapping.IndentLevel - 1)
                {
                    parent = ordered[i];
                    break;
                }
            }
        }
        if (parent is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Parent:** {parent.RawHeading} (`/{parent.Keys.FirstOrDefault() ?? parent.PrimaryKey}#{parent.AnchorId}`)");
            sb.AppendLine();
        }

        // Previous section at same indent level in the same chapter
        SiteMapping? prev = null;
        for (int i = idx - 1; i >= 0; i--)
        {
            if (ordered[i].ChapterNumber != mapping.ChapterNumber) break;
            if (ordered[i].IndentLevel == mapping.IndentLevel)
            {
                prev = ordered[i];
                break;
            }
        }
        if (prev is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Previous:** {prev.RawHeading} (`/{prev.Keys.FirstOrDefault() ?? prev.PrimaryKey}#{prev.AnchorId}`)");
        }

        // Next section at same indent level in the same chapter
        SiteMapping? next = null;
        for (int i = idx + 1; i < ordered.Count; i++)
        {
            if (ordered[i].ChapterNumber != mapping.ChapterNumber) break;
            if (ordered[i].IndentLevel < mapping.IndentLevel) break;
            if (ordered[i].IndentLevel == mapping.IndentLevel)
            {
                next = ordered[i];
                break;
            }
        }
        if (next is not null)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Next:** {next.RawHeading} (`/{next.Keys.FirstOrDefault() ?? next.PrimaryKey}#{next.AnchorId}`)");
        }

        // Siblings: all siblings sharing the same parent
        if (parent is not null)
        {
            int parentIdx = ordered.FindIndex(m => ReferenceEquals(m, parent));
            var siblings = new List<SiteMapping>();
            for (int i = parentIdx + 1; i < ordered.Count; i++)
            {
                if (ordered[i].ChapterNumber != mapping.ChapterNumber) break;
                if (ordered[i].IndentLevel < mapping.IndentLevel) break;
                if (ordered[i].IndentLevel == mapping.IndentLevel && ordered[i] != mapping)
                {
                    siblings.Add(ordered[i]);
                }
            }
            if (siblings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("**Sibling sections:**");
                foreach (var s in siblings)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  - {s.RawHeading} (`/{s.Keys.FirstOrDefault() ?? s.PrimaryKey}#{s.AnchorId}`)");
                }
            }
        }

        return sb.ToString();
    }

    [McpServerTool(Title = "Get Chapter Summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Get a structural overview of a book chapter: its top-level section headings in reading order, and the coding guidelines associated with that chapter. Useful for understanding what a chapter covers before diving in.")]
    public string GetChapterSummary(
        [Description("The chapter number (e.g., 5 for Chapter 5).")] int chapter)
    {
        var chapterMappings = _siteMappingService.SiteMappings
            .Where(m => m.ChapterNumber == chapter)
            .OrderBy(m => m.PageNumber)
            .ThenBy(m => m.OrderOnPage)
            .ToList();

        if (chapterMappings.Count == 0)
        {
            return $"Chapter {chapter} not found in the book's table of contents.";
        }

        string chapterTitle = chapterMappings.First().ChapterTitle;

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"# Chapter {chapter}: {chapterTitle}");
        sb.AppendLine();
        sb.AppendLine("## Sections");

        foreach (var m in chapterMappings.Where(m => m.IndentLevel <= 1))
        {
            string indent = m.IndentLevel == 0 ? "" : "  ";
            string link = $"`/{m.Keys.FirstOrDefault() ?? m.PrimaryKey}#{m.AnchorId}`";
            sb.AppendLine(CultureInfo.InvariantCulture, $"{indent}- {m.RawHeading} ({link})");
        }

        var guidelines = _guidelinesService.Guidelines
            .Where(g => g.ChapterNumber == chapter)
            .ToList();

        if (guidelines.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Guidelines in this Chapter");
            foreach (var g in guidelines)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"- **[{g.Type.ToDisplayString()}]** {g.Guideline}");
            }
        }

        return sb.ToString();
    }

    private static void ExtractNodeContent(HtmlNode node, StringBuilder sb)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            string text = HtmlEntity.DeEntitize(node.InnerText).Trim();
            if (!string.IsNullOrEmpty(text))
            {
                sb.AppendLine(text);
            }
            return;
        }

        if (node.Name is not ("div" or "p" or "ul" or "ol" or "li" or "span")) return;

        string nodeClass = node.GetAttributeValue("class", "");

        // Code block: extract heading + code lines
        if (nodeClass.Contains("code-block-section"))
        {
            var headingNode = node.SelectSingleNode(".//div[contains(@class,'code-block-heading')]");
            if (headingNode is not null)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"\n**{HtmlEntity.DeEntitize(headingNode.InnerText).Trim()}**");
            }

            sb.AppendLine("```csharp");
            var codeLines = node.SelectNodes(".//div[contains(@class,'code-line')]");
            if (codeLines is not null)
            {
                foreach (var lineClone in codeLines.Select(line => line.CloneNode(deep: true)))
                {
                    var lineNumberSpan = lineClone.SelectSingleNode(".//span[contains(@class,'code-line-number')]");
                    lineNumberSpan?.Remove();
                    sb.AppendLine(HtmlEntity.DeEntitize(lineClone.InnerText));
                }
            }
            sb.AppendLine("```");
            return;
        }

        // Paragraphs and other content
        if (node.Name is "p" || nodeClass.Contains("paragraph"))
        {
            string text = HtmlEntity.DeEntitize(node.InnerText).Trim();
            if (!string.IsNullOrEmpty(text))
            {
                sb.AppendLine(text);
                sb.AppendLine();
            }
            return;
        }

        // Recurse into other divs (skill-topic-block, etc.)
        foreach (HtmlNode child in node.ChildNodes)
        {
            ExtractNodeContent(child, sb);
        }
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex AnchorIdRegex();
}
