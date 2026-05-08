using Microsoft.Extensions.Caching.Memory;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Validates that a <c>previousResponseId</c> supplied by the client was issued to the requesting user,
/// preventing cross-user conversation access.
/// </summary>
/// <remarks>
/// Each response ID is cached individually as <c>responseId → userId</c> with a 2-hour sliding expiry
/// matching the OpenAI Responses API conversation window. This eliminates arbitrary eviction (no
/// per-user <see cref="HashSet{T}"/> cap needed) and gives each ID its own natural TTL.
///
/// <para><b>Cache-miss = allow</b> (graceful degradation): in a single-instance deployment a cache
/// miss occurs only after a server restart. In a multi-instance deployment the cache is in-process only,
/// so a request routed to a different instance will always miss. In both scenarios the user is allowed
/// to continue rather than being locked out. This is an accepted trade-off for a soft control on an
/// already-authenticated, rate-limited endpoint. Upgrade to <c>IDistributedCache</c> (e.g., Redis) to
/// close this gap in multi-instance deployments.</para>
/// </remarks>
public sealed class ResponseIdValidationService(IMemoryCache cache)
{
    /// <summary>
    /// Records a newly issued response ID as belonging to the specified user.
    /// </summary>
    public void RecordResponseId(string? userId, string? responseId)
    {
        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(responseId))
        {
            return;
        }

        // Create a fresh options instance per call — MemoryCacheEntryOptions has mutable
        // list properties (ExpirationTokens, PostEvictionCallbacks) and sharing a static
        // instance would cause future additions to affect all entries.
        // Size = 1 unit per entry is required when AddMemoryCache sets a SizeLimit.
        var entryOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromHours(2))
            .SetSize(1);
        cache.Set(ResponseKey(responseId), userId, entryOptions);
    }

    /// <summary>
    /// Returns <c>true</c> when the response ID is valid for this user.
    /// </summary>
    /// <remarks>
    /// Returns <c>true</c> for null/empty IDs (start of a new conversation) and on cache misses
    /// (graceful degradation — see class remarks). Returns <c>false</c> only when the cache has a
    /// positive entry for the ID but it is owned by a different user.
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

        if (!cache.TryGetValue(ResponseKey(responseId), out string? ownerUserId))
        {
            return true; // Cache miss — allow (graceful degradation; see class remarks)
        }

        return string.Equals(ownerUserId, userId, StringComparison.Ordinal);
    }

    private static string ResponseKey(string responseId) => $"chat_rid:{responseId}";
}
