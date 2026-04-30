using System.Security.Cryptography;
using System.Text;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace EssentialCSharp.Web.Services;

public class McpApiTokenService(EssentialCSharpWebContext db)
{
    public const int DefaultLifetimeMonths = 6;
    public static string MaxExpiryValidationMessage => $"MCP tokens can expire at most {DefaultLifetimeMonths} months from today.";

    public sealed record ResolvedMcpApiToken(Guid TokenId, string UserId);

    public static DateOnly GetDefaultExpiryDate(DateTime? referenceTimeUtc = null)
        => DateOnly.FromDateTime(referenceTimeUtc ?? DateTime.UtcNow).AddMonths(DefaultLifetimeMonths);

    public static DateTime GetDefaultExpirationUtc(DateTime? referenceTimeUtc = null)
        => GetDefaultExpiryDate(referenceTimeUtc).ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

    /// <summary>Returns SHA-256 hash of the raw token as a byte array (varbinary(32)).</summary>
    public static byte[] HashToken(string rawToken)
        => SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));

    /// <summary>Generates a cryptographically random opaque token with "mcp_" prefix.</summary>
    public static string GenerateRawToken()
        => "mcp_" + Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Creates a new named API token for the specified user.
    /// Returns the raw token (shown once — never stored).
    /// </summary>
    public async Task<(string RawToken, McpApiToken Entity)> CreateTokenAsync(
        string userId,
        string name,
        DateTime? expiresAt = null,
        CancellationToken cancellationToken = default)
    {
        string raw = GenerateRawToken();
        DateTime createdAt = DateTime.UtcNow;
        DateTime effectiveExpiration = ResolveExpiration(expiresAt, createdAt);

        var entity = new McpApiToken
        {
            UserId = userId,
            Name = name,
            TokenHash = HashToken(raw),
            CreatedAt = createdAt,
            ExpiresAt = effectiveExpiration,
        };
        db.McpApiTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return (raw, entity);
    }

    private static DateTime ResolveExpiration(DateTime? requestedExpirationUtc, DateTime createdAtUtc)
    {
        DateTime maxExpiration = GetDefaultExpirationUtc(createdAtUtc);
        if (requestedExpirationUtc is null)
            return maxExpiration;

        if (requestedExpirationUtc > maxExpiration)
            throw new ArgumentOutOfRangeException(nameof(requestedExpirationUtc), MaxExpiryValidationMessage);

        return requestedExpirationUtc.Value;
    }

    /// <summary>
    /// Revokes a token by ID. Validates ownership to prevent cross-user revocation.
    /// Returns false if token not found or user doesn't own it.
    /// </summary>
    public async Task<bool> RevokeTokenAsync(
        Guid tokenId,
        string userId,
        CancellationToken cancellationToken = default)
    {
        int rows = await db.McpApiTokens
            .Where(t => t.Id == tokenId && t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);
        return rows > 0;
    }

    /// <summary>Returns all tokens for the user (metadata only — no raw values).</summary>
    public Task<List<McpApiToken>> GetUserTokensAsync(
        string userId,
        CancellationToken cancellationToken = default)
        => db.McpApiTokens
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Resolves a raw token string to its owning user if the token exists and is currently valid.
    /// Does not update LastUsedAt or UsageCount.
    /// </summary>
    public Task<ResolvedMcpApiToken?> ResolveValidTokenAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        byte[] hash = HashToken(rawToken);
        DateTime now = DateTime.UtcNow;

        return db.McpApiTokens
            .AsNoTracking()
            .Where(t => t.TokenHash == hash
                     && t.RevokedAt == null
                     && (t.ExpiresAt == null || t.ExpiresAt > now))
            .Select(t => new ResolvedMcpApiToken(t.Id, t.UserId))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Records successful authenticated token usage. Returns false if the token is no longer valid.
    /// </summary>
    public async Task<bool> MarkTokenUsedAsync(
        Guid tokenId,
        CancellationToken cancellationToken = default)
    {
        DateTime now = DateTime.UtcNow;
        int rows = await db.McpApiTokens
            .Where(t => t.Id == tokenId
                     && t.RevokedAt == null
                     && (t.ExpiresAt == null || t.ExpiresAt > now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.LastUsedAt, now)
                .SetProperty(t => t.UsageCount, t => t.UsageCount + 1),
                cancellationToken);

        return rows > 0;
    }
}
