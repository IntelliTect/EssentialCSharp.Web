using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Services;

public class SiteMappingService : ISiteMappingService
{
    public IList<SiteMapping> SiteMappings { get; }

    public SiteMappingService(IWebHostEnvironment webHostEnvironment)
    {
        string path = Path.Combine(webHostEnvironment.ContentRootPath, "Chapters", "sitemap.json");
        List<SiteMapping>? siteMappings = System.Text.Json.JsonSerializer.Deserialize<List<SiteMapping>>(File.OpenRead(path)) ?? throw new InvalidOperationException("No table of contents found");
        SiteMappings = siteMappings;
    }

    public IEnumerable<SiteMappingDto> GetTocData()
    {
        return SiteMappings.GroupBy(x => x.ChapterNumber).OrderBy(x => x.Key).Select(x =>
        {
            IEnumerable<SiteMapping> orderedX = x.OrderBy(i => i.PageNumber).ThenBy(i => i.OrderOnPage);
            SiteMapping firstX = orderedX.First();
            return new SiteMappingDto()
            {
                Level = 0,
                Key = [firstX.Keys.First()],
                Href = $"{firstX.Keys.First()}#{firstX.AnchorId}",
                Title = $"Chapter {x.Key}: {firstX.ChapterTitle}",
                Items = GetItems(orderedX.Skip(1), 1)
            };
        }
        );
    }

    private static IEnumerable<SiteMappingDto> GetItems(IEnumerable<SiteMapping> chapterItems, int indentLevel)
    {
        return chapterItems
           // Examine all items up until we move up to a level higher than where we're starting,
           // which would indicate that we've reached the end of the entries nested under `indentationLevel`
           .TakeWhile(i => i.IndentLevel >= indentLevel)
            // Of all the multi-level descendants we found, take only those at the current level that we're wanting to render.
            .Where(i => i.IndentLevel == indentLevel)
            .Select(i => new SiteMappingDto()
            {
                Level = indentLevel,
                Key = i.Keys,
                Href = $"{i.Keys.First()}#{i.AnchorId}",
                Title = i.RawHeading,
                // Any children of this node will be /after/ this node,
                // so skip any items that are /before/ the current node.
                Items = GetItems(chapterItems.SkipWhile(q => i.Keys.First() != q.Keys.First()).Skip(1), indentLevel + 1)
            });
    }
}

/// <summary>
/// A model for a site mapping to transport between the tooling and the web app so the web app knows where to find the content a user is looking for.
/// </summary>
public class SiteMapping
{
    /// <param name="keys">The key for the site mapping.</param>
    /// <param name="pagePath">The path to the actual HTML page.</param>
    /// <param name="chapterNumber">The chapter number of the content.</param>
    /// <param name="pageNumber">The page number of the content.</param>
    /// <param name="orderOnPage">A number indicating order on a page.</param>
    /// <param name="chapterTitle">The title of the chapter.</param>
    /// <param name="rawHeading">The raw heading of the content.</param>
    /// <param name="anchorId">The anchor ID of the content. This allows you to know what anchors are on a page and get to that specific area.</param>
    /// <param name="indentLevel">The indent level of the content. This is used to determine how the content is displayed in the table of contents.</param>
    /// <param name="contentHash">A hash of the content. This is used to determine if the content has changed and needs to be updated.</param>
    /// <param name="includeInSitemapXml"><see cref="IncludeInSitemapXml"/></param>
    public SiteMapping(List<string> keys, string[] pagePath, int chapterNumber, int pageNumber, int orderOnPage, string chapterTitle, string rawHeading, string? anchorId,
        int indentLevel, string? contentHash = null, bool includeInSitemapXml = true)
    {
        Keys = keys;
        PagePath = pagePath;
        ChapterNumber = chapterNumber;
        PageNumber = pageNumber;
        OrderOnPage = orderOnPage;
        ChapterTitle = chapterTitle;
        RawHeading = rawHeading;
        ContentHash = contentHash;
        AnchorId = anchorId;
        IndentLevel = indentLevel;
        IncludeInSitemapXml = includeInSitemapXml;
    }

    /// <summary>
    /// The key for the site mapping.
    /// </summary>
    public List<string> Keys { get; }
    /// <summary>
    /// The path to the actual HTML page.
    /// </summary>
    public string[] PagePath { get; }
    /// <summary>
    /// The chapter number of the content.
    /// </summary>
    public int ChapterNumber { get; }
    /// <summary>
    /// The page number of the content.
    /// </summary>
    public int PageNumber { get; }
    /// <summary>
    /// A number indicating order on a page.
    /// </summary>
    public int OrderOnPage { get; set; }
    /// <summary>
    /// The title of the chapter.
    /// </summary>
    public string ChapterTitle { get; }
    /// <summary>
    /// The raw heading of the content.
    /// </summary>
    public string RawHeading { get; set; }
    /// <summary>
    /// The hash of the content. This is used to determine if the content has changed and needs to be updated.
    /// This should be a base64 encoded string of the hash.
    /// This should not be null by the time parsing is complete.
    /// </summary>
    public string? ContentHash { get; set; }
    /// <summary>
    /// The anchor ID of the content. This allows you to know what anchors are on a page and get to that specific area.
    /// </summary>
    public string? AnchorId { get; }
    /// <summary>
    /// The indent level of the content. This is used to determine how the content is displayed in the table of contents.
    /// </summary>
    public int IndentLevel { get; }
    /// <summary>
    /// Whether or not to include this in the sitemap. This is used to determine if a page should be included in the sitemap XML file.
    /// Only the top most heading of a page should be included in the sitemap, and the rest will be canonical links.
    /// This is only used to parse the Sitemap XML file.
    /// </summary>
    public bool IncludeInSitemapXml { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is SiteMapping other)
        {
            return Keys == other.Keys &&
                   PagePath?.SequenceEqual(other.PagePath) == true &&
                   ChapterNumber == other.ChapterNumber &&
                   PageNumber == other.PageNumber &&
                   OrderOnPage == other.OrderOnPage &&
                   ChapterTitle == other.ChapterTitle &&
                   RawHeading == other.RawHeading &&
                   AnchorId == other.AnchorId &&
                   IndentLevel == other.IndentLevel &&
                   IncludeInSitemapXml == other.IncludeInSitemapXml;
        }
        return false;
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        // HashCode.Combine doesn't work for more than 8 items.
        HashCode hash = new();
        hash.Add(Keys);
        hash.Add(PagePath);
        hash.Add(ChapterNumber);
        hash.Add(PageNumber);
        hash.Add(OrderOnPage);
        hash.Add(ChapterTitle);
        hash.Add(RawHeading);
        hash.Add(AnchorId);
        hash.Add(IndentLevel);
        hash.Add(IncludeInSitemapXml);
        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{ChapterNumber}.{PageNumber} - {ChapterTitle} - Raw Heading: {RawHeading} - Hash: {ContentHash} {Environment.NewLine} {string.Join(", ", Keys)}";
    }
}
