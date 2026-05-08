using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ListingSourceCodeController : ControllerBase
{
    private readonly IListingSourceCodeService _ListingSourceCodeService;
    private readonly ILogger<ListingSourceCodeController> _Logger;

    public ListingSourceCodeController(
        IListingSourceCodeService listingSourceCodeService,
        ILogger<ListingSourceCodeController> logger)
    {
        _ListingSourceCodeService = listingSourceCodeService;
        _Logger = logger;
    }

    [HttpGet("chapter/{chapter}/listing/{listing}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetListing(int chapter, int listing)
    {
        var result = await _ListingSourceCodeService.GetListingAsync(chapter, listing);
        
        if (result == null)
        {
            return NotFound(new { error = $"Listing {chapter}.{listing} not found." });
        }

        return Ok(result);
    }

    [HttpGet("chapter/{chapter}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetListingsByChapter(int chapter)
    {
        var results = await _ListingSourceCodeService.GetListingsByChapterAsync(chapter);
        return Ok(results);
    }
}
