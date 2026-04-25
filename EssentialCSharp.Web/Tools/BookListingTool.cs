using System.ComponentModel;
using System.Globalization;
using System.Text;
using EssentialCSharp.Web.Services;
using ModelContextProtocol.Server;

namespace EssentialCSharp.Web.Tools;

[McpServerToolType]
public sealed class BookListingTool
{
    private readonly IListingSourceCodeService _listingService;
    private readonly ISiteMappingService _siteMappingService;

    public BookListingTool(IListingSourceCodeService listingService, ISiteMappingService siteMappingService)
    {
        _listingService = listingService;
        _siteMappingService = siteMappingService;
    }

    [McpServerTool(Title = "Get Listing Source Code", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Retrieve the complete source code for a specific numbered listing from the Essential C# book. Example: chapter=5, listing=3 retrieves Listing 5.3. Returns the code and its file type.")]
    public async Task<string> GetListingSourceCode(
        [Description("The chapter number containing the listing (e.g., 5 for Chapter 5).")] int chapter,
        [Description("The listing number within the chapter (e.g., 3 for Listing 5.3).")] int listing,
        CancellationToken cancellationToken = default)
    {
        var response = await _listingService.GetListingAsync(chapter, listing);
        if (response is null)
        {
            return $"Listing {chapter}.{listing} not found. Verify that both the chapter and listing numbers are correct.";
        }

        string langHint = response.FileExtension == "cs" ? "csharp" : response.FileExtension;
        return $"## Listing {response.ChapterNumber}.{response.ListingNumber}\n\n```{langHint}\n{response.Content}\n```";
    }

    [McpServerTool(Title = "Search Listings By Code", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false),
     Description("Search all code listings in the Essential C# book for a specific code pattern, keyword, or identifier. Searches actual C# source code (not prose). Useful for finding examples of Task.WhenAll, yield return, IDisposable, pattern matching, and similar code constructs.")]
    public async Task<string> SearchListingsByCode(
        [Description("The code pattern or keyword to search for in listing source code (case-insensitive substring match).")] string pattern,
        [Description("Maximum number of matching listings to return (1–20, default 10).")] int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "Pattern must not be empty.";
        }

        maxResults = Math.Clamp(maxResults, 1, 20);

        var distinctChapters = _siteMappingService.SiteMappings
            .Select(m => m.ChapterNumber)
            .Distinct()
            .OrderBy(n => n);

        var sb = new StringBuilder();
        int found = 0;

        foreach (int chapterNumber in distinctChapters)
        {
            if (found >= maxResults) break;
            cancellationToken.ThrowIfCancellationRequested();

            var listings = await _listingService.GetListingsByChapterAsync(chapterNumber);
            foreach (var listing in listings)
            {
                if (found >= maxResults) break;
                if (listing.Content.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    string langHint = listing.FileExtension == "cs" ? "csharp" : listing.FileExtension;
                    sb.AppendLine(CultureInfo.InvariantCulture, $"### Listing {listing.ChapterNumber}.{listing.ListingNumber}");
                    sb.AppendLine(CultureInfo.InvariantCulture, $"```{langHint}");
                    sb.AppendLine(listing.Content);
                    sb.AppendLine("```");
                    sb.AppendLine();
                    found++;
                }
            }
        }

        if (found == 0)
        {
            return $"No listings found containing '{pattern}'.";
        }

        sb.Insert(0, $"# Listings Containing '{pattern}' ({found} result{(found == 1 ? "" : "s")})\n\n");
        return sb.ToString();
    }
}
