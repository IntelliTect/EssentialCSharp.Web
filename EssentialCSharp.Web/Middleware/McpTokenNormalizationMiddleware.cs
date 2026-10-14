using System.Security.Claims;
using EssentialCSharp.Web.Auth;
using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Middleware;

/// <summary>
/// Normalizes the <see cref="HttpContext.User"/> for MCP requests before rate limiting.
/// Valid MCP bearer tokens resolve to an MCP user principal; missing or invalid tokens
/// fall back to an anonymous principal so they bucket by IP rather than inheriting the
/// site's cookie principal.
/// </summary>
public class McpTokenNormalizationMiddleware(RequestDelegate next)
{
    // Cached singleton — avoids allocating new ClaimsPrincipal/ClaimsIdentity per request
    // for the common case of unauthenticated or non-bearer MCP requests.
    private static readonly ClaimsPrincipal AnonymousPrincipal = new(new ClaimsIdentity());

    public async Task InvokeAsync(HttpContext context, McpApiTokenService tokenService)
    {
        McpApiTokenService.ResolvedMcpApiToken? resolvedToken = null;
        if (McpBearerAuthentication.TryGetRawToken(context.Request, out string? rawToken))
        {
            resolvedToken = await tokenService.ResolveValidTokenAsync(rawToken, context.RequestAborted);
            McpBearerAuthentication.StoreResolution(context, resolvedToken);
        }

        context.User = resolvedToken is not null
            ? McpBearerAuthentication.CreatePrincipal(resolvedToken.UserId)
            : AnonymousPrincipal;

        await next(context);
    }
}
