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

    public async Task<RecordSessionResult> RecordSessionAsync(
        string userId, IReadOnlyList<ReadingInterval> intervals, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(intervals);

        foreach (ReadingInterval interval in intervals)
        {
            if (wordCountService.GetPageWordCount(interval.PageKey) <= 0)
            {
                return new RecordSessionResult(null, interval.PageKey);
            }
        }

        DateTime now = DateTime.UtcNow;

        // One consistent reference speed for the whole batch.
        ReadingProfileSummary current = await GetProfileAsync(userId, cancellationToken);
        double referenceWpm = current.Wpm ?? 0;

        long deltaWords = 0;
        long deltaSeconds = 0;
        foreach (ReadingInterval interval in intervals)
        {
            int words = Math.Min(interval.WordsRead, wordCountService.GetPageWordCount(interval.PageKey));
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

        return new RecordSessionResult(await GetProfileAsync(userId, cancellationToken), null);
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
    /// Atomically creates the profile row or adds the deltas to it in a single statement,
    /// so concurrent requests for the same user never lose an increment or violate the key.
    /// </summary>
    private async Task AddToProfileAsync(
        string userId, long words, long seconds, DateTime now, CancellationToken cancellationToken)
    {
        if (context.Database.IsSqlServer())
        {
            await context.Database.ExecuteSqlAsync(
                $"""
                MERGE UserReadingProfiles WITH (HOLDLOCK) AS target
                USING (SELECT {userId} AS UserId) AS source ON target.UserId = source.UserId
                WHEN MATCHED THEN UPDATE SET
                    TotalWordsRead = target.TotalWordsRead + {words},
                    TotalActiveSeconds = target.TotalActiveSeconds + {seconds},
                    UpdatedAtUtc = {now}
                WHEN NOT MATCHED THEN INSERT (UserId, TotalWordsRead, TotalActiveSeconds, UpdatedAtUtc)
                    VALUES ({userId}, {words}, {seconds}, {now});
                """,
                cancellationToken);
        }
        else // SQLite (used by the integration tests)
        {
            await context.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO UserReadingProfiles (UserId, TotalWordsRead, TotalActiveSeconds, UpdatedAtUtc)
                VALUES ({userId}, {words}, {seconds}, {now})
                ON CONFLICT (UserId) DO UPDATE SET
                    TotalWordsRead = TotalWordsRead + excluded.TotalWordsRead,
                    TotalActiveSeconds = TotalActiveSeconds + excluded.TotalActiveSeconds,
                    UpdatedAtUtc = excluded.UpdatedAtUtc
                """,
                cancellationToken);
        }
    }
}
