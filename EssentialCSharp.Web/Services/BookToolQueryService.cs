using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Models;
using ModelContextProtocol;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public sealed class BookToolQueryService : IBookToolQueryService
{
    private readonly ISiteMappingService _siteMappingService;
    private readonly IGuidelinesService _guidelinesService;
    private readonly string _siteUrl;

    public BookToolQueryService(
        ISiteMappingService siteMappingService,
        IGuidelinesService guidelinesService,
        IOptions<SiteSettings> siteSettings)
    {
        _siteMappingService = siteMappingService;
        _guidelinesService = guidelinesService;
        _siteUrl = string.IsNullOrWhiteSpace(siteSettings.Value.BaseUrl)
            ? "https://essentialcsharp.com"
            : siteSettings.Value.BaseUrl.TrimEnd('/');
    }

    public ChapterListToolResult GetChapterList()
    {
        List<BookTocItemResult> chapters = _siteMappingService.GetTocData()
            .Select(MapTocItem)
            .ToList();

        return new ChapterListToolResult("Essential C# - Table of Contents", chapters);
    }

    public ChapterSectionsToolResult GetChapterSections(int chapter)
    {
        List<SiteMapping> sections = GetChapterMappings(chapter);
        if (sections.Count == 0)
        {
            throw new McpException($"Chapter {chapter} not found. Use GetChapterList to see all available chapters.");
        }

        List<BookSectionReferenceResult> sectionResults = sections
            .Select(ToSectionReference)
            .ToList();

        return new ChapterSectionsToolResult(chapter, sections[0].ChapterTitle, sectionResults);
    }

    public BookSectionReferenceResult GetDirectContentUrl(string sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            throw new McpException("Section key must not be empty. Use GetChapterSections or GetChapterList to discover valid section slugs.");
        }

        SiteMapping mapping = ResolveSection(sectionKey);
        return ToSectionReference(mapping);
    }

    public NavigationContextToolResult GetNavigationContext(string sectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey))
        {
            throw new McpException("Section key must not be empty. Use GetChapterSections to discover valid section slugs.");
        }

        SiteMapping mapping = ResolveSection(sectionKey);
        List<SiteMapping> ordered = GetOrderedMappings();

        int index = ordered.FindIndex(candidate => ReferenceEquals(candidate, mapping));
        if (index < 0)
        {
            throw new McpException($"Section '{sectionKey}' could not be located in the ordered mapping list.");
        }

        List<SiteMapping> breadcrumb = [];
        int targetIndent = mapping.IndentLevel;
        for (int i = index - 1; i >= 0 && targetIndent > 0; i--)
        {
            if (ordered[i].ChapterNumber != mapping.ChapterNumber)
            {
                break;
            }

            if (ordered[i].IndentLevel < targetIndent)
            {
                breadcrumb.Insert(0, ordered[i]);
                targetIndent = ordered[i].IndentLevel;
            }
        }

        SiteMapping? parent = null;
        if (mapping.IndentLevel > 0)
        {
            for (int i = index - 1; i >= 0; i--)
            {
                if (ordered[i].ChapterNumber != mapping.ChapterNumber)
                {
                    break;
                }

                if (ordered[i].IndentLevel == mapping.IndentLevel - 1)
                {
                    parent = ordered[i];
                    break;
                }
            }
        }

        SiteMapping? previous = null;
        for (int i = index - 1; i >= 0; i--)
        {
            if (ordered[i].ChapterNumber != mapping.ChapterNumber)
            {
                break;
            }

            if (ordered[i].IndentLevel == mapping.IndentLevel)
            {
                previous = ordered[i];
                break;
            }
        }

        SiteMapping? next = null;
        for (int i = index + 1; i < ordered.Count; i++)
        {
            if (ordered[i].ChapterNumber != mapping.ChapterNumber || ordered[i].IndentLevel < mapping.IndentLevel)
            {
                break;
            }

            if (ordered[i].IndentLevel == mapping.IndentLevel)
            {
                next = ordered[i];
                break;
            }
        }

        List<BookSectionReferenceResult> siblings = [];
        if (parent is not null)
        {
            int parentIndex = ordered.FindIndex(candidate => ReferenceEquals(candidate, parent));
            for (int i = parentIndex + 1; i < ordered.Count; i++)
            {
                if (ordered[i].ChapterNumber != mapping.ChapterNumber || ordered[i].IndentLevel < mapping.IndentLevel)
                {
                    break;
                }

                if (ordered[i].IndentLevel == mapping.IndentLevel && !ReferenceEquals(ordered[i], mapping))
                {
                    siblings.Add(ToSectionReference(ordered[i]));
                }
            }
        }

        return new NavigationContextToolResult(
            ToSectionReference(mapping),
            breadcrumb.Select(ToSectionReference).ToList(),
            parent is null ? null : ToSectionReference(parent),
            previous is null ? null : ToSectionReference(previous),
            next is null ? null : ToSectionReference(next),
            siblings);
    }

    public ChapterSummaryToolResult GetChapterSummary(int chapter)
    {
        List<SiteMapping> chapterMappings = GetChapterMappings(chapter);
        if (chapterMappings.Count == 0)
        {
            throw new McpException($"Chapter {chapter} not found in the book's table of contents.");
        }

        List<BookSectionReferenceResult> sections = chapterMappings
            .Where(mapping => mapping.IndentLevel <= 1)
            .Select(ToSectionReference)
            .ToList();

        List<BookGuidelineSummaryResult> guidelines = _guidelinesService.Guidelines
            .Where(guideline => guideline.ChapterNumber == chapter)
            .Select(guideline => new BookGuidelineSummaryResult(
                guideline.Type.ToDisplayString(),
                guideline.Guideline,
                guideline.ChapterNumber,
                guideline.ChapterTitle ?? string.Empty,
                guideline.SanitizedSubsection))
            .ToList();

        return new ChapterSummaryToolResult(chapter, chapterMappings[0].ChapterTitle, sections, guidelines);
    }

    private SiteMapping ResolveSection(string sectionKey) =>
        _siteMappingService.SiteMappings.Find(sectionKey)
        ?? throw new McpException($"Section '{sectionKey}' not found. Use GetChapterSections or GetChapterList to discover valid section slugs.");

    private List<SiteMapping> GetOrderedMappings() =>
        _siteMappingService.SiteMappings
            .OrderBy(mapping => mapping.ChapterNumber)
            .ThenBy(mapping => mapping.PageNumber)
            .ThenBy(mapping => mapping.OrderOnPage)
            .ToList();

    private List<SiteMapping> GetChapterMappings(int chapter) =>
        _siteMappingService.SiteMappings
            .Where(mapping => mapping.ChapterNumber == chapter)
            .OrderBy(mapping => mapping.PageNumber)
            .ThenBy(mapping => mapping.OrderOnPage)
            .ToList();

    private BookTocItemResult MapTocItem(SiteMappingDto item)
    {
        string href = NormalizeHref(item.Href);
        return new BookTocItemResult(
            item.Key,
            item.Title,
            href,
            BuildUrl(href),
            item.Level,
            item.Items.Select(MapTocItem).ToList());
    }

    private BookSectionReferenceResult ToSectionReference(SiteMapping mapping)
    {
        string key = mapping.Keys.FirstOrDefault() ?? mapping.PrimaryKey;
        string href = BuildHref(key, mapping.AnchorId);
        return new BookSectionReferenceResult(
            key,
            mapping.RawHeading,
            mapping.ChapterNumber,
            mapping.ChapterTitle,
            mapping.IndentLevel,
            mapping.AnchorId,
            href,
            BuildUrl(href));
    }

    private static string BuildHref(string key, string? anchorId) =>
        anchorId is null ? $"/{key}" : $"/{key}#{anchorId}";

    private static string NormalizeHref(string href) =>
        href.StartsWith('/') ? href : $"/{href}";

    private string BuildUrl(string href) => $"{_siteUrl}{href}";
}
