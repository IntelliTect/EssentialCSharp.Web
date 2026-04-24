using System.Security.Claims;
using System.Text.Encodings.Web;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Auth;

/// <summary>
/// Authenticates MCP requests via opaque "mcp_..." bearer tokens stored in the database.
/// Reads Authorization: Bearer mcp_... header, validates via McpApiTokenService, and
/// builds a ClaimsPrincipal with NameIdentifier set to the token owner's user ID.
/// </summary>
public class McpApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    McpApiTokenService tokenService)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string? authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        string rawToken = authHeader["Bearer ".Length..].Trim();
        if (!rawToken.StartsWith("mcp_", StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var (token, userId) = await tokenService.ValidateTokenAsync(rawToken, Context.RequestAborted);
        if (token is null || userId is null)
            return AuthenticateResult.Fail("Invalid or revoked MCP token.");

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
        };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }
}
