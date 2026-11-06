using System.Security.Claims;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Authorize]
[Route("api/reading")]
[ReadingAntiforgeryFailureHeader]
public class ReadingController(
    IReadingProfileService readingProfileService,
    IOptions<ReadingOptions> options) : ControllerBase
{
    public const string WriteRateLimitPolicy = "ReadingWrite";

    private const int MaxSessionRequestBytes = 32 * 1024;

    [HttpPost("session")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
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

        int maxIntervals = options.Value.MaxIntervalsPerRequest;
        if (request.Intervals.Count > maxIntervals)
        {
            ModelState.AddModelError(
                nameof(ReadingSessionRequest.Intervals),
                $"A session may contain at most {maxIntervals} intervals.");
            return ValidationProblem(ModelState);
        }

        if (request.Intervals.Any(i => i is null))
        {
            ModelState.AddModelError(nameof(ReadingSessionRequest.Intervals), "Intervals must not contain null entries.");
            return ValidationProblem(ModelState);
        }

        int maxActiveSeconds = options.Value.MaxActiveSecondsPerInterval;
        if (request.Intervals.Any(i => i.ActiveSeconds > maxActiveSeconds))
        {
            ModelState.AddModelError(
                nameof(ReadingIntervalRequest.ActiveSeconds),
                $"An interval may contain at most {maxActiveSeconds} active seconds.");
            return ValidationProblem(ModelState);
        }

        ReadingInterval[] intervals = request.Intervals
            .Select(i => new ReadingInterval(i.PageKey, i.WordsRead, i.ActiveSeconds))
            .ToArray();

        RecordSessionResult result = await readingProfileService.RecordSessionAsync(userId, intervals, cancellationToken);
        if (result.Profile is null)
        {
            ModelState.AddModelError(nameof(ReadingIntervalRequest.PageKey), $"Unknown page key '{result.UnknownPageKey}'.");
            return ValidationProblem(ModelState);
        }

        return result.Profile;
    }

    [HttpPost("reset")]
    [EnableRateLimiting(WriteRateLimitPolicy)]
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
