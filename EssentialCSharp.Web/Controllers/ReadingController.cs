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
    private const int MaxSessionIntervals = 50;
    private const int MaxSessionRequestBytes = 32 * 1024;

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
    [RequestSizeLimit(MaxSessionRequestBytes)]
    public async Task<IActionResult> PostSession(
        [FromBody] IEnumerable<ReadingIntervalDto> intervals,
        CancellationToken cancellationToken)
    {
        string userId = GetUserId();

        if (intervals is null)
        {
            return BadRequest("intervals is required");
        }

        List<ReadingIntervalDto> intervalList = intervals.Take(MaxSessionIntervals + 1).ToList();
        if (intervalList.Count > MaxSessionIntervals)
        {
            return BadRequest($"A session may contain at most {MaxSessionIntervals} intervals.");
        }

        if (intervalList.Count == 0)
        {
            return Ok();
        }

        // This read supplies the clamping reference; aggregate writes below use atomic SQL increments.
        UserReadingProfile? profile = await context.UserReadingProfiles
            .AsNoTracking()
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

            if (intervalWpm > MaxWpmHardCutoff)
            {
                LogIntervalDiscarded(logger, interval.PageKey, intervalWpm, MaxWpmHardCutoff);
                continue;
            }

            int effectiveWords = interval.WordsRead;
            int effectiveSeconds = interval.ActiveSeconds;
            if (referenceWpm > 0 && intervalWpm < SlowOutlierFactor * referenceWpm)
            {
                effectiveSeconds = Math.Max(
                    1,
                    (int)Math.Ceiling(effectiveWords / (SlowOutlierFactor * referenceWpm) * 60.0));
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

        profile = await PersistSessionAsync(userId, deltaWords, deltaSeconds, activities, cancellationToken);
        return Ok(new
        {
            totalWordsRead = profile?.TotalWordsRead ?? 0,
            totalActiveSeconds = profile?.TotalActiveSeconds ?? 0,
            wpm = profile?.DeriveWpm()
        });
    }

    private async Task<UserReadingProfile?> PersistSessionAsync(
        string userId,
        long deltaWords,
        long deltaSeconds,
        List<ReadingActivity> activities,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            UserReadingProfile? newProfile = null;
            bool attemptedProfileInsert = false;

            try
            {
                if (deltaWords > 0 || deltaSeconds > 0)
                {
                    int updatedProfiles = await context.UserReadingProfiles
                        .Where(p => p.UserId == userId)
                        .ExecuteUpdateAsync(
                            setters => setters
                                .SetProperty(p => p.TotalWordsRead, p => p.TotalWordsRead + deltaWords)
                                .SetProperty(p => p.TotalActiveSeconds, p => p.TotalActiveSeconds + deltaSeconds)
                                .SetProperty(p => p.UpdatedAtUtc, DateTime.UtcNow),
                            cancellationToken);

                    if (updatedProfiles == 0)
                    {
                        attemptedProfileInsert = true;
                        newProfile = new UserReadingProfile
                        {
                            UserId = userId,
                            TotalWordsRead = deltaWords,
                            TotalActiveSeconds = deltaSeconds,
                            UpdatedAtUtc = DateTime.UtcNow
                        };
                        context.UserReadingProfiles.Add(newProfile);
                        await context.SaveChangesAsync(cancellationToken);
                    }
                }

                if (activities.Count > 0)
                {
                    await context.ReadingActivities.AddRangeAsync(activities, cancellationToken);
                }

                await context.SaveChangesAsync(cancellationToken);
                await TrimReadingActivitiesAsync(userId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                return await context.UserReadingProfiles
                    .AsNoTracking()
                    .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);
            }
            catch (DbUpdateException) when (attempt == 0 && attemptedProfileInsert)
            {
                await transaction.RollbackAsync(cancellationToken);
                context.Entry(newProfile!).State = EntityState.Detached;

                // A concurrent first request may have inserted this user's profile.
                bool profileCreatedConcurrently = await context.UserReadingProfiles
                    .AnyAsync(p => p.UserId == userId, cancellationToken);
                if (!profileCreatedConcurrently)
                {
                    throw;
                }
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }

        throw new InvalidOperationException("Unable to persist the reading session after a concurrent profile insert.");
    }

    private async Task TrimReadingActivitiesAsync(string userId, CancellationToken cancellationToken)
    {
        IQueryable<int> retainedActivityIds = context.ReadingActivities
            .Where(activity => activity.UserId == userId)
            .OrderByDescending(activity => activity.RecordedAtUtc)
            .ThenByDescending(activity => activity.Id)
            .Take(MaxReadingActivityRowsPerUser)
            .Select(activity => activity.Id);

        await context.ReadingActivities
            .Where(activity =>
                activity.UserId == userId &&
                !retainedActivityIds.Contains(activity.Id))
            .ExecuteDeleteAsync(cancellationToken);
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
