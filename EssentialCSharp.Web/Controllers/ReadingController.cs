using System.Security.Claims;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web.Controllers;

[ApiController]
[Route("api/reading")]
public partial class ReadingController(
    EssentialCSharpWebContext context,
    IWordCountService wordCountService,
    ILogger<ReadingController> logger) : ControllerBase
{
    // Algorithm constants (mirrors Kindle's ReadingTimer values).
    private const int MaxWpmHardCutoff = 900;
    private const double SlowOutlierFactor = 0.25;
    private const int MaxReadingActivityRowsPerUser = 500;

    // High-performance logger messages (CA1848).
    [LoggerMessage(Level = LogLevel.Debug, Message = "Discarding interval for {PageKey}: {Wpm:F0} WPM exceeds hard cutoff of {Cutoff}")]
    private static partial void LogIntervalDiscarded(ILogger logger, string pageKey, double wpm, int cutoff);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Clamping slow interval for {PageKey}: {Wpm:F0} WPM → effective {EffSeconds}s")]
    private static partial void LogIntervalClamped(ILogger logger, string pageKey, double wpm, int effSeconds);

    // -------------------------------------------------------------------------
    // GET /api/reading/book-stats  (public — used by anonymous clients too)
    // -------------------------------------------------------------------------

    [HttpGet("book-stats")]
    public IActionResult GetBookStats()
    {
        var chapters = wordCountService.GetChapterWordCounts()
            .Select(c => new { chapterNumber = c.ChapterNumber, wordCount = c.WordCount });

        return Ok(new
        {
            totalWordCount = wordCountService.GetBookWordCount(),
            chapters
        });
    }

    // -------------------------------------------------------------------------
    // GET /api/reading/profile  (authenticated)
    // -------------------------------------------------------------------------

    [HttpGet("profile")]
    [Authorize]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        string userId = GetUserId();

        UserReadingProfile? profile = await context.UserReadingProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile is null)
        {
            return Ok(new { totalWordsRead = 0L, totalActiveSeconds = 0L, wpm = (double?)null });
        }

        return Ok(new
        {
            totalWordsRead = profile.TotalWordsRead,
            totalActiveSeconds = profile.TotalActiveSeconds,
            wpm = profile.DeriveWpm()
        });
    }

    // -------------------------------------------------------------------------
    // POST /api/reading/session  (authenticated)
    // -------------------------------------------------------------------------

    public record ReadingIntervalDto(
        string PageKey,
        int ActiveSeconds,
        int WordsRead,
        bool Completed);

    [HttpPost("session")]
    [Authorize]
    public async Task<IActionResult> PostSession(
        [FromBody] IEnumerable<ReadingIntervalDto> intervals,
        CancellationToken cancellationToken)
    {
        string userId = GetUserId();

        if (intervals is null)
        {
            return BadRequest("intervals is required");
        }

        List<ReadingIntervalDto> intervalList = intervals.ToList();
        if (intervalList.Count == 0)
        {
            return Ok();
        }

        // Load or create profile (used as reference WPM for outlier clamping).
        UserReadingProfile? profile = await context.UserReadingProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        double referenceWpm = profile?.DeriveWpm() ?? 0;

        long deltaWords = 0;
        long deltaSeconds = 0;
        var activities = new List<ReadingActivity>(intervalList.Count);

        foreach (ReadingIntervalDto interval in intervalList)
        {
            if (interval.ActiveSeconds <= 0 || interval.WordsRead <= 0)
            {
                continue;
            }

            double intervalWpm = interval.WordsRead / (interval.ActiveSeconds / 60.0);

            // Hard discard: too fast to be genuine reading.
            if (intervalWpm > MaxWpmHardCutoff)
            {
                LogIntervalDiscarded(logger, interval.PageKey, intervalWpm, MaxWpmHardCutoff);
                continue;
            }

            // Soft clamp: interval is implausibly slow relative to reader's own rate.
            int effectiveWords = interval.WordsRead;
            int effectiveSeconds = interval.ActiveSeconds;
            if (referenceWpm > 0 && intervalWpm < SlowOutlierFactor * referenceWpm)
            {
                // Clamp: keep the words, shrink the time so effective WPM = 0.25 × referenceWpm.
                effectiveSeconds = (int)Math.Round(effectiveWords / (SlowOutlierFactor * referenceWpm) * 60.0);
                LogIntervalClamped(logger, interval.PageKey, intervalWpm, effectiveSeconds);
            }

            deltaWords += effectiveWords;
            deltaSeconds += effectiveSeconds;

            activities.Add(new ReadingActivity
            {
                UserId = userId,
                PageKey = interval.PageKey,
                RecordedAtUtc = DateTime.UtcNow,
                ActiveSeconds = interval.ActiveSeconds,
                WordsRead = interval.WordsRead,
                Completed = interval.Completed
            });
        }

        // Persist inside a single transaction: insert detail rows, update aggregate, trim retention.
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            if (activities.Count > 0)
            {
                await context.ReadingActivities.AddRangeAsync(activities, cancellationToken);
            }

            if (deltaWords > 0 || deltaSeconds > 0)
            {
                if (profile is null)
                {
                    profile = new UserReadingProfile
                    {
                        UserId = userId,
                        TotalWordsRead = deltaWords,
                        TotalActiveSeconds = deltaSeconds,
                        UpdatedAtUtc = DateTime.UtcNow
                    };
                    context.UserReadingProfiles.Add(profile);
                }
                else
                {
                    profile.TotalWordsRead += deltaWords;
                    profile.TotalActiveSeconds += deltaSeconds;
                    profile.UpdatedAtUtc = DateTime.UtcNow;
                }
            }

            await context.SaveChangesAsync(cancellationToken);

            // Trim ReadingActivity to the newest 500 rows for this user.
            // We do this after SaveChanges so the new rows are visible in the sub-query.
            await context.Database.ExecuteSqlRawAsync(
                """
                DELETE FROM [ReadingActivities]
                WHERE [UserId] = {0}
                  AND [Id] NOT IN (
                    SELECT TOP ({1}) [Id]
                    FROM [ReadingActivities]
                    WHERE [UserId] = {0}
                    ORDER BY [RecordedAtUtc] DESC
                  )
                """,
                [userId, MaxReadingActivityRowsPerUser],
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return Ok(new
        {
            totalWordsRead = profile?.TotalWordsRead ?? 0,
            totalActiveSeconds = profile?.TotalActiveSeconds ?? 0,
            wpm = profile?.DeriveWpm()
        });
    }

    // -------------------------------------------------------------------------
    // POST /api/reading/reset  (authenticated)
    // -------------------------------------------------------------------------

    [HttpPost("reset")]
    [Authorize]
    public async Task<IActionResult> Reset(CancellationToken cancellationToken)
    {
        string userId = GetUserId();

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await context.ReadingActivities
                .Where(a => a.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

            await context.UserReadingProfiles
                .Where(p => p.UserId == userId)
                .ExecuteDeleteAsync(cancellationToken);

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return Ok();
    }

    // -------------------------------------------------------------------------

    private string GetUserId() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("Authenticated user has no NameIdentifier claim.");
}
