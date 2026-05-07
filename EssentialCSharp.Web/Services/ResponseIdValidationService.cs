using Microsoft.Extensions.Caching.Memory;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Validates that a <c>previousResponseId</c> supplied by the client belongs to the requesting user,
/// preventing cross-user conversation access.
/// </summary>
/// <remarks>
/// IDs are stored per-user in an in-process <see cref="IMemoryCache"/> with a 2-hour sliding expiry.
/// On a cache miss (e.g., first request after a server restart or in a multi-instance deployment) the
/// request is allowed to proceed so that users are not locked out — this is an acceptable graceful
/// degradation trade-off for a soft security control.
/// </remarks>
public sealed class ResponseIdValidationService(IMemoryCache cache)
{
    private static readonly MemoryCacheEntryOptions _CacheOptions =
        new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromHours(2));

    /// <summary>
    /// Records a newly issued response ID as belonging to the specified user.
    /// </summary>
    public void RecordResponseId(string? userId, string? responseId)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(responseId))
        {
            return;
        }

        var key = CacheKey(userId);
        var ids = cache.GetOrCreate(key, _ => new HashSet<string>(StringComparer.Ordinal))!;
        lock (ids)
        {
            ids.Add(responseId);
        }
        // Re-set to refresh the sliding expiry
        cache.Set(key, ids, _CacheOptions);
    }

    /// <summary>
    /// Returns <c>true</c> when the response ID is valid for this user.
    /// </summary>
    /// <remarks>
    /// Always returns <c>true</c> for null/empty IDs (start of a new conversation) and on cache misses
    /// (graceful degradation). Returns <c>false</c> only when the cache has a positive entry for the
    /// user but the supplied ID is not in that set.
    /// </remarks>
    public bool ValidateResponseId(string? userId, string? responseId)
    {
        if (string.IsNullOrEmpty(responseId))
        {
            return true; // New conversation — nothing to validate
        }

        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        if (!cache.TryGetValue(CacheKey(userId), out HashSet<string>? ids))
        {
            return true; // Cache miss — allow (graceful degradation)
        }

        lock (ids!)
        {
            return ids.Contains(responseId);
        }
    }

    private static string CacheKey(string userId) => $"chat_response_ids:{userId}";
}
