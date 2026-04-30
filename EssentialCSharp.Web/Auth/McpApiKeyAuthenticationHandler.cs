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
    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        if (!Response.Headers.ContainsKey("WWW-Authenticate"))
        {
            Response.Headers.Append("WWW-Authenticate", "Bearer");
        }

        return Task.CompletedTask;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!McpBearerAuthentication.TryGetRawToken(Request, out string? rawToken))
        {
            return AuthenticateResult.NoResult();
        }

        McpApiTokenService.ResolvedMcpApiToken? resolvedToken;
        if (McpBearerAuthentication.TryGetStoredResolution(Context, out resolvedToken))
        {
            if (resolvedToken is null)
            {
                return AuthenticateResult.Fail("Invalid or revoked MCP token.");
            }
        }
        else
        {
            resolvedToken = await tokenService.ResolveValidTokenAsync(rawToken, Context.RequestAborted);
            if (resolvedToken is null)
            {
                return AuthenticateResult.Fail("Invalid or revoked MCP token.");
            }
        }

        if (!await tokenService.MarkTokenUsedAsync(resolvedToken.TokenId, Context.RequestAborted))
        {
            return AuthenticateResult.Fail("Invalid or revoked MCP token.");
        }

        ClaimsPrincipal principal = McpBearerAuthentication.CreatePrincipal(resolvedToken.UserId);
        var ticket = new AuthenticationTicket(principal, McpBearerAuthentication.Scheme);
        return AuthenticateResult.Success(ticket);
    }
}
