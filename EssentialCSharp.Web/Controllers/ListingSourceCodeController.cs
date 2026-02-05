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

    [HttpGet("{chapterNumber}/{listingNumber}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetListing(int chapterNumber, int listingNumber)
    {
        var result = await _ListingSourceCodeService.GetListingAsync(chapterNumber, listingNumber);
        
        if (result == null)
        {
            return NotFound(new { error = $"Listing {chapterNumber}.{listingNumber} not found." });
        }

        return Ok(result);
    }

    [HttpGet("{chapterNumber}")]
    [ResponseCache(Duration = 3600, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> GetListingsByChapter(int chapterNumber)
    {
        var results = await _ListingSourceCodeService.GetListingsByChapterAsync(chapterNumber);
        return Ok(results);
    }
}
