using System.Security.Cryptography;
using System.Text;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace EssentialCSharp.Web.Services;

public class McpApiTokenService(EssentialCSharpWebContext db)
{
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
        var entity = new McpApiToken
        {
            UserId = userId,
            Name = name,
            TokenHash = HashToken(raw),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt,
        };
        db.McpApiTokens.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return (raw, entity);
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
    /// Validates a raw token string. Updates LastUsedAt and UsageCount on success.
    /// Returns (token, userId) on success, or (null, null) on failure.
    /// </summary>
    public async Task<(McpApiToken? Token, string? UserId)> ValidateTokenAsync(
        string rawToken,
        CancellationToken cancellationToken = default)
    {
        byte[] hash = HashToken(rawToken);
        DateTime now = DateTime.UtcNow;

        // Initial read — early exit for completely unknown tokens before hitting the update round-trip
        McpApiToken? token = await db.McpApiTokens
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (token is null) return (null, null);

        // Atomic guard: re-checks validity at the moment of update so a concurrent
        // revoke between the read above and this update cannot slip through (TOCTOU fix).
        int rows = await db.McpApiTokens
            .Where(t => t.Id == token.Id
                     && t.RevokedAt == null
                     && (t.ExpiresAt == null || t.ExpiresAt > now))
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.LastUsedAt, now)
                .SetProperty(t => t.UsageCount, t => t.UsageCount + 1),
                cancellationToken);

        if (rows == 0) return (null, null);

        return (token, token.UserId);
    }
}
