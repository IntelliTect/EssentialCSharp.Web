using System.ComponentModel;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using ModelContextProtocol.Protocol;
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

        return ToTextResult(new ListingSourceCodeResult(
                response.ChapterNumber,
                response.ListingNumber,
                ToLanguageHint(response.FileExtension),
                response.Content))
            .ToMcpString();
    }

    [McpServerTool(
        Title = "Search Listings By Code",
        ReadOnly = true,
        Destructive = false,
        Idempotent = true,
        OpenWorld = false,
        UseStructuredContent = true,
        OutputSchemaType = typeof(ListingSearchToolResult)),
     Description("Search all code listings in the Essential C# book for a specific code pattern, keyword, or identifier. Searches actual C# source code (not prose). Useful for finding examples of Task.WhenAll, yield return, IDisposable, pattern matching, and similar code constructs.")]
    public async Task<CallToolResult> SearchListingsByCode(
        [Description("The code pattern or keyword to search for in listing source code (case-insensitive substring match).")] string pattern,
        [Description("Maximum number of matching listings to return (1–20).")] int maxResults = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return McpToolResultFactory.CreateError("Pattern must not be empty.");
        }

        string trimmedPattern = pattern.Trim();
        bool isKnownOperator = trimmedPattern is "=>" or "??" or "?." or "::" or "??=" or "==" or "!=" or "<=" or ">=" or "&&" or "||";
        if (!isKnownOperator && trimmedPattern.Count(char.IsLetterOrDigit) < 2)
        {
            return McpToolResultFactory.CreateError("Pattern must contain at least two letters or digits, or be a recognized C# operator (=>, ??, ?., ::, ??=, ==, !=, <=, >=, &&, ||).");
        }

        maxResults = Math.Clamp(maxResults, 1, 20);

        var distinctChapters = _siteMappingService.SiteMappings
            .Select(m => m.ChapterNumber)
            .Distinct()
            .OrderBy(n => n);

        List<ListingSourceCodeResult> matches = [];

        foreach (int chapterNumber in distinctChapters)
        {
            if (matches.Count >= maxResults) break;
            cancellationToken.ThrowIfCancellationRequested();

            var listings = await _listingService.GetListingsByChapterAsync(chapterNumber);
            foreach (var listing in listings)
            {
                if (matches.Count >= maxResults) break;
                if (listing.Content.Contains(trimmedPattern, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(new ListingSourceCodeResult(
                        listing.ChapterNumber,
                        listing.ListingNumber,
                        ToLanguageHint(listing.FileExtension),
                        listing.Content));
                }
            }
        }

        ListingSearchToolResult structuredResult = new(trimmedPattern, matches);
        if (matches.Count == 0)
        {
            return McpToolResultFactory.CreateHybridResult(
                $"No listings found containing '{trimmedPattern}'.",
                structuredResult);
        }

        return McpToolResultFactory.CreateHybridResult(
            new ListingSearchTextResult(trimmedPattern, matches.Select(ToTextResult).ToList()).ToMcpString(),
            structuredResult);
    }

    private static string ToLanguageHint(string fileExtension) => fileExtension == "cs" ? "csharp" : fileExtension;

    private static ListingSourceCodeTextResult ToTextResult(ListingSourceCodeResult listing) =>
        new(listing.ChapterNumber, listing.ListingNumber, listing.LanguageHint, listing.Content);
}
