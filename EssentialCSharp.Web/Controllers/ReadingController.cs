using System.Security.Claims;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace EssentialCSharp.Web.Controllers;

// The rate limit is class-level so every action is limited: /api/reading is exempt from the global limiter in Program.cs.
[ApiController]
[Authorize]
[Route("api/reading")]
[ReadingAntiforgeryFailureHeader]
[EnableRateLimiting(WriteRateLimitPolicy)]
public sealed class ReadingController(IReadingProfileService readingProfileService) : ControllerBase
{
    public const string WriteRateLimitPolicy = "ReadingWrite";

    private const int MaxSessionRequestBytes = 32 * 1024;

    [HttpPost("session")]
    [RequestSizeLimit(MaxSessionRequestBytes)]
    [ProducesResponseType<ReadingProfileSummary>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ReadingProfileSummary>> PostSession(
        [FromBody] ReadingSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (GetUserId() is not { } userId)
        {
            return Unauthorized();
        }

        ReadingInterval[] intervals = request.Intervals
            .Select(i => new ReadingInterval(i.PageKey, i.WordsRead, i.ActiveSeconds))
            .ToArray();

        // Intervals for unknown pages are skipped by the service rather than failing the request.
        return await readingProfileService.RecordSessionAsync(userId, intervals, cancellationToken);
    }

    [HttpPost("reset")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        if (GetUserId() is not { } userId)
        {
            return Unauthorized();
        }

        await readingProfileService.ResetAsync(userId, cancellationToken);
        return NoContent();
    }

    private string? GetUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
}
