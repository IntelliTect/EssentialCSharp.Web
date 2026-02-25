using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace EssentialCSharp.Web.Services;

// TODO: Track issued jti claims in the database to enable per-token revocation
// TODO: Add a user-facing revocation UI on the MCP Access page
// TODO: Consider migration to MCP SDK's native OAuth 2.0 flow for token management
public class McpTokenService
{
    private readonly string _SigningKey;
    private readonly string _Issuer;
    private readonly string _Audience;
    private readonly int _ExpirationDays;

    public McpTokenService(IConfiguration configuration)
    {
        _SigningKey = configuration["Mcp:SigningKey"]
            ?? throw new InvalidOperationException("Mcp:SigningKey is not configured. Set it via user-secrets or environment variables.");
        _Issuer = configuration["Mcp:Issuer"] ?? "EssentialCSharp";
        _Audience = configuration["Mcp:Audience"] ?? "EssentialCSharp.Mcp";
        _ExpirationDays = int.TryParse(configuration["Mcp:TokenExpirationDays"], out int days) ? days : 7;
    }

    public (string Token, DateTime ExpiresAt) GenerateToken(string userId, string? userName, string? email)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddDays(_ExpirationDays);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
        };

        if (!string.IsNullOrEmpty(userName))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Name, userName));
        }
        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
        }

        var token = new JwtSecurityToken(
            issuer: _Issuer,
            audience: _Audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }

    public TokenValidationParameters GetTokenValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = _Issuer,
        ValidAudience = _Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_SigningKey)),
    };
}
