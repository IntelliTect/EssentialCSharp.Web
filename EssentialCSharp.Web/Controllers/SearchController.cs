using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("SearchEndpoint")]
public class SearchController : ControllerBase
{
    private readonly ITypesenseSearchService _searchService;
    private readonly ILogger<SearchController> _logger;

    public SearchController(ITypesenseSearchService searchService, ILogger<SearchController> logger)
    {
        _searchService = searchService;
        _logger = logger;
    }

    /// <summary>
    /// Search for content using Typesense
    /// </summary>
    /// <param name="request">The search request parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search results</returns>
    [HttpPost]
    public async Task<IActionResult> Search([FromBody] SearchRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return BadRequest(new { error = "Search query cannot be empty." });
        }

        if (request.Query.Length > 500)
        {
            return BadRequest(new { error = "Search query is too long. Maximum 500 characters." });
        }

        try
        {
            var result = await _searchService.SearchAsync(
                request.Query, 
                request.Page, 
                Math.Min(request.PerPage, 50), // Limit max results per page
                cancellationToken);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed for query: {Query}", request.Query);
            return StatusCode(500, new { error = "Search service temporarily unavailable." });
        }
    }

    /// <summary>
    /// Search for content using GET method for simple queries
    /// </summary>
    /// <param name="q">Search query</param>
    /// <param name="page">Page number (default: 1)</param>
    /// <param name="per_page">Results per page (default: 10, max: 50)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search results</returns>
    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] string q, 
        [FromQuery] int page = 1, 
        [FromQuery] int perPage = 10,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest(new { error = "Search query cannot be empty." });
        }

        var request = new SearchRequest
        {
            Query = q,
            Page = Math.Max(1, page),
            PerPage = Math.Min(Math.Max(1, perPage), 50)
        };

        return await Search(request, cancellationToken);
    }

    /// <summary>
    /// Get search health status
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health status</returns>
    [HttpGet("health")]
    public async Task<IActionResult> Health(CancellationToken cancellationToken = default)
    {
        try
        {
            var isHealthy = await _searchService.IsHealthyAsync(cancellationToken);
            
            if (isHealthy)
            {
                return Ok(new { status = "healthy", timestamp = DateTime.UtcNow });
            }
            else
            {
                return StatusCode(503, new { status = "unhealthy", timestamp = DateTime.UtcNow });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return StatusCode(503, new { status = "error", timestamp = DateTime.UtcNow, error = ex.Message });
        }
    }
}