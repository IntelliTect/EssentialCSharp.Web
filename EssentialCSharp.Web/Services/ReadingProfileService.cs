using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public sealed partial class ReadingProfileService(
    EssentialCSharpWebContext context,
    IWordCountService wordCountService,
    IOptions<ReadingOptions> options,
    ILogger<ReadingProfileService> logger) : IReadingProfileService
{
    private readonly ReadingOptions _options = options.Value;

    [LoggerMessage(Level = LogLevel.Debug, Message = "Ignored interval for {PageKey}: {Words} words in {Seconds}s is not credited")]
    private static partial void LogIntervalIgnored(ILogger logger, string pageKey, int words, int seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped reading credit: the authenticated user no longer exists")]
    private static partial void LogUserMissing(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignored interval for page {PageKey}: unknown page or no countable words")]
    private static partial void LogUnknownPage(ILogger logger, string pageKey);

    /// <remarks>
    /// Not idempotent: if a client retries a request whose response was lost, the same intervals are credited
    /// twice. This is accepted (no idempotency store); the effect is a small skew in the derived WPM.
    /// The reference WPM used for the slow-outlier rule is read once before the batch is written, so concurrent
    /// requests from the same user may evaluate against a slightly stale reference. This is also accepted.
    /// A batch already in flight when <see cref="ResetAsync"/> runs is credited after the reset (requests carry no
    /// epoch to tell them apart); the client discards its own unsent data on reset, so only that narrow window remains.
    /// </remarks>
    public async Task<ReadingProfileSummary> RecordSessionAsync(
        string userId, IReadOnlyList<ReadingInterval> intervals, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intervals);

        DateTime now = DateTime.UtcNow;

        // One consistent reference speed for the whole batch.
        ReadingProfileSummary current = await GetProfileAsync(userId, cancellationToken);
        // A reader without a derived speed yet is held to the default speed, so a first (or just-reset) batch of
        // idle-inflated intervals cannot set their speed unchecked.
        double referenceWpm = current.Wpm ?? _options.DefaultWpm;

        long deltaWords = 0;
        long deltaSeconds = 0;
        for (int i = 0; i < intervals.Count; i++)
        {
            ReadingInterval interval = intervals[i];
            // An unknown or empty page (for example a key renamed by a content deploy) skips only this interval:
            // rejecting the batch would make the client drop every interval queued with it.
            int pageWords = wordCountService.GetPageWordCount(interval.PageKey);
            if (pageWords <= 0)
            {
                LogUnknownPage(logger, interval.PageKey);
                continue;
            }

            int words = Math.Min(interval.WordsRead, pageWords);
            ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(words, interval.ActiveSeconds, referenceWpm, _options);
            if (credit is null)
            {
                LogIntervalIgnored(logger, interval.PageKey, words, interval.ActiveSeconds);
                continue;
            }

            deltaWords += credit.Value.Words;
            deltaSeconds += credit.Value.Seconds;
        }

        if (deltaWords > 0 || deltaSeconds > 0)
        {
            await AddToProfileAsync(userId, deltaWords, deltaSeconds, now, cancellationToken);
        }

        return await GetProfileAsync(userId, cancellationToken);
    }

    public async Task<ReadingProfileSummary> GetProfileAsync(string userId, CancellationToken cancellationToken)
    {
        var totals = await context.UserReadingProfiles
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => new { p.TotalWordsRead, p.TotalActiveSeconds })
            .FirstOrDefaultAsync(cancellationToken);

        return totals is null
            ? new ReadingProfileSummary(0, 0, null)
            : new ReadingProfileSummary(
                totals.TotalWordsRead,
                totals.TotalActiveSeconds,
                UserReadingProfile.DeriveWpm(totals.TotalWordsRead, totals.TotalActiveSeconds));
    }

    public async Task ResetAsync(string userId, CancellationToken cancellationToken) =>
        await context.UserReadingProfiles
            .Where(p => p.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

    /// <summary>
    /// Adds the deltas to the user's profile, creating the row on first use, without a provider-specific upsert:
    /// try an atomic <c>UPDATE ... SET x = x + delta</c>; if no row exists insert one; if a concurrent request
    /// inserted it first (duplicate key), retry the update once. Concurrent requests therefore never lose an increment.
    /// </summary>
    private async Task AddToProfileAsync(
        string userId, long words, long seconds, DateTime now, CancellationToken cancellationToken)
    {
        if (await TryAddToExistingAsync(userId, words, seconds, now, cancellationToken) > 0)
        {
            return;
        }

        var created = new UserReadingProfile
        {
            UserId = userId,
            TotalWordsRead = words,
            TotalActiveSeconds = seconds,
            UpdatedAtUtc = now,
        };
        context.UserReadingProfiles.Add(created);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Most likely a concurrent request created the row between our UPDATE and INSERT.
            context.Entry(created).State = EntityState.Detached;
            if (await TryAddToExistingAsync(userId, words, seconds, now, cancellationToken) == 0)
            {
                // Not a duplicate key. A user deleted while their cookie is still valid cannot own a profile row
                // (foreign key); drop the credit rather than failing every flush, which the client would retry forever.
                if (!await context.Users.AnyAsync(u => u.Id == userId, cancellationToken))
                {
                    LogUserMissing(logger);
                    return;
                }

                throw;
            }
        }
    }

    private Task<int> TryAddToExistingAsync(
        string userId, long words, long seconds, DateTime now, CancellationToken cancellationToken) =>
        context.UserReadingProfiles
            .Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(p => p.TotalWordsRead, p => p.TotalWordsRead + words)
                    .SetProperty(p => p.TotalActiveSeconds, p => p.TotalActiveSeconds + seconds)
                    .SetProperty(p => p.UpdatedAtUtc, now),
                cancellationToken);
}
