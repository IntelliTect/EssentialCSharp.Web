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
            IEnumerable<SiteMapping> orderedGrouping = x.OrderBy(i => i.PageNumber).ThenBy(i => i.OrderOnPage);
            SiteMapping firstElement = orderedGrouping.First();
            return new SiteMappingDto()
            {
                Level = 0,
                Key = firstElement.Keys.First(),
                Href = $"{firstElement.Keys.First()}#{firstElement.AnchorId}",
                Title = $"Chapter {x.Key}: {firstElement.ChapterTitle}",
                Items = GetItems(orderedGrouping.Skip(1), 1)
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
                Key = i.Keys.First(),
                Href = $"{i.Keys.First()}#{i.AnchorId}",
                Title = i.RawHeading,
                // Any children of this node will be /after/ this node,
                // so skip any items that are /before/ the current node.
                Items = GetItems(chapterItems.SkipWhile(q => i.Keys.First() != q.Keys.First()).Skip(1), indentLevel + 1)
            });
    }
}
