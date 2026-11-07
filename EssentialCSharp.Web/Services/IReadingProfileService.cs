namespace EssentialCSharp.Web.Services;

/// <summary>A single reading interval reported by a client.</summary>
public sealed record ReadingInterval(string PageKey, int WordsRead, int ActiveSeconds);

/// <summary>
/// A user's aggregate reading totals. Serialized as-is by the reading API
/// (<c>totalWordsRead</c>, <c>totalActiveSeconds</c>, <c>wpm</c>).
/// </summary>
public sealed record ReadingProfileSummary(long TotalWordsRead, long TotalActiveSeconds, double? Wpm);

public interface IReadingProfileService
{
    Task<ReadingProfileSummary> RecordSessionAsync(
        string userId, IReadOnlyList<ReadingInterval> intervals, CancellationToken cancellationToken);

    Task<ReadingProfileSummary> GetProfileAsync(string userId, CancellationToken cancellationToken);

    Task ResetAsync(string userId, CancellationToken cancellationToken);
}
