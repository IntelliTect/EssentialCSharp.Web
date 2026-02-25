using System.ComponentModel;
using System.Globalization;
using System.Text;
using EssentialCSharp.Chat.Common.Services;
using EssentialCSharp.Web.Services;
using ModelContextProtocol.Server;

namespace EssentialCSharp.Web.Tools;

[McpServerToolType]
public sealed class BookSearchTool
{
    private readonly AISearchService? _SearchService;
    private readonly ISiteMappingService _SiteMappingService;

    public BookSearchTool(IServiceProvider serviceProvider, ISiteMappingService siteMappingService)
    {
        _SearchService = serviceProvider.GetService<AISearchService>();
        _SiteMappingService = siteMappingService;
    }

    [McpServerTool, Description("Search the Essential C# book content using semantic vector search. Returns relevant text chunks with chapter and heading context. Use this to find information about C# programming concepts covered in the book.")]
    public async Task<string> SearchBookContent(
        [Description("The search query describing the C# concept or topic to find in the book.")] string query,
        CancellationToken cancellationToken = default)
    {
        if (_SearchService is null)
        {
            return "Book search is not available in this environment (AI services are not configured).";
        }

        var results = await _SearchService.ExecuteVectorSearch(query);

        var sb = new StringBuilder();
        int resultCount = 0;

        await foreach (var result in results.WithCancellation(cancellationToken))
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

    [McpServerTool, Description("Get the table of contents for the Essential C# book, listing all chapters and their sections with navigation links.")]
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
}
