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
/// <para>This service creates and owns a dedicated <see cref="MemoryCache"/> with a bounded
/// <c>SizeLimit</c>, rather than using the shared <c>IMemoryCache</c>. This prevents the
/// <c>SizeLimit</c> from applying globally (which would require every other cache user in the app to
/// call <c>SetSize</c>) while still bounding memory use for this service.</para>
///
/// <para><b>Cache-miss = allow</b> (graceful degradation): a cache miss can occur after a server
/// restart, when a sliding-expiration entry times out (2 h of inactivity), under memory pressure
/// (OS or GC-triggered eviction), or when the configured <c>SizeLimit</c> forces eviction of the
/// least-recently-used entries. In a multi-instance deployment the in-process cache is not shared, so
/// requests routed to a different instance always miss. In all these scenarios the user is allowed to
/// continue rather than being locked out. This is an accepted trade-off for a soft control on an
/// already-authenticated, rate-limited endpoint. Upgrade to <c>IDistributedCache</c> (e.g., Redis) to
/// close this gap in multi-instance or high-eviction deployments.</para>
/// </remarks>
public sealed class ResponseIdValidationService : IDisposable
{
    private const int CacheSizeLimit = 10_000;

    private readonly IMemoryCache _cache;
    private readonly bool _ownsCache;
    private bool _disposed;

    /// <summary>
    /// Production constructor. Creates and owns a dedicated <see cref="MemoryCache"/> with a bounded
    /// <see cref="MemoryCacheOptions.SizeLimit"/> so the app-wide shared <c>IMemoryCache</c> is unaffected.
    /// </summary>
    public ResponseIdValidationService()
        : this(new MemoryCache(new MemoryCacheOptions { SizeLimit = CacheSizeLimit }), ownsCache: true) { }

    /// <summary>
    /// Testing constructor. The caller owns and disposes the supplied cache.
    /// </summary>
    public ResponseIdValidationService(IMemoryCache cache)
        : this(cache, ownsCache: false) { }

    private ResponseIdValidationService(IMemoryCache cache, bool ownsCache)
    {
        _cache = cache;
        _ownsCache = ownsCache;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_ownsCache && _cache is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

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
        // SetSize(1) accounts for this entry against the dedicated cache's SizeLimit.
        var entryOptions = new MemoryCacheEntryOptions()
            .SetSlidingExpiration(TimeSpan.FromHours(2))
            .SetSize(1);
        _cache.Set(ResponseKey(responseId), userId, entryOptions);
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

        if (!_cache.TryGetValue(ResponseKey(responseId), out string? ownerUserId))
        {
            return true; // Cache miss — allow (graceful degradation; see class remarks)
        }

        return string.Equals(ownerUserId, userId, StringComparison.Ordinal);
    }

    private static string ResponseKey(string responseId) => $"chat_rid:{responseId}";
}
