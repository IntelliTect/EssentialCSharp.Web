using System.ComponentModel;
using System.Text.RegularExpressions;
using EssentialCSharp.Chat.Common.Services;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using ModelContextProtocol.Server;

namespace EssentialCSharp.Web.Tools;

[McpServerToolType]
public sealed partial class BookContentTool
{
    private readonly ISiteMappingService _siteMappingService;
    private readonly IBookToolQueryService _bookToolQueryService;
    private readonly IListingSourceCodeService _listingService;
    private readonly IWebHostEnvironment _environment;
    private readonly AISearchService? _searchService;

    public BookContentTool(
        ISiteMappingService siteMappingService,
        IBookToolQueryService bookToolQueryService,
        IListingSourceCodeService listingService,
        IWebHostEnvironment environment,
        IServiceProvider serviceProvider)
    {
        _siteMappingService = siteMappingService;
        _bookToolQueryService = bookToolQueryService;
        _listingService = listingService;
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

        SectionContentExtractionResult extraction = SectionContentExtractionResult.FromHtml(mapping, htmlContent, maxChars);
        return extraction.ErrorMessage ?? extraction.Content!.ToMcpString();
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
        List<RelatedBookExplanationTextResult> explanations = [];

        if (_searchService is not null)
        {
            string query = $"Chapter {chapter} listing {listing} {response.Content[..Math.Min(200, response.Content.Length)]}";
            var contextResults = await _searchService.ExecuteVectorSearch(query, cancellationToken: cancellationToken);
            if (contextResults.Count > 0)
            {
                foreach (var result in contextResults.Take(3))
                {
                    explanations.Add(new RelatedBookExplanationTextResult(
                        result.Record.Heading,
                        result.Record.ChapterNumber,
                        result.Record.ChunkText));
                }
            }
        }

        return new ListingWithContextTextResult(
            new ListingSourceCodeTextResult(response.ChapterNumber, response.ListingNumber, langHint, response.Content),
            explanations).ToMcpString();
    }

    [McpServerTool(Title = "Get Navigation Context", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
     Description("Get the navigation context for a book section: its breadcrumb path, the previous and next sections, its parent section, and its sibling sections. Useful for understanding where a section sits in the book's structure.")]
    public NavigationContextToolResult GetNavigationContext(
        [Description("The section slug/key (e.g., 'hello-world'). Use GetChapterSections to get valid slugs.")] string sectionKey) =>
        _bookToolQueryService.GetNavigationContext(sectionKey);

    [McpServerTool(Title = "Get Chapter Summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true),
     Description("Get a structural overview of a book chapter: its top-level section headings in reading order, and the coding guidelines associated with that chapter. Useful for understanding what a chapter covers before diving in.")]
    public ChapterSummaryToolResult GetChapterSummary(
        [Description("The chapter number (e.g., 5 for Chapter 5).")] int chapter) =>
        _bookToolQueryService.GetChapterSummary(chapter);

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex AnchorIdRegex();
}
