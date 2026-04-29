using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Auth;

internal static class McpBearerAuthentication
{
    internal sealed record ResolutionResult(McpApiTokenService.ResolvedMcpApiToken? Token);

    private static readonly object ResolutionResultKey = new();

    public const string Scheme = "McpBearer";

    public static bool TryGetRawToken(HttpRequest request, [NotNullWhen(true)] out string? rawToken)
    {
        rawToken = null;

        string? authHeader = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string candidate = authHeader["Bearer ".Length..].Trim();
        if (!candidate.StartsWith("mcp_", StringComparison.Ordinal))
        {
            return false;
        }

        rawToken = candidate;
        return true;
    }

    public static ClaimsPrincipal CreatePrincipal(string userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], Scheme));

    public static void StoreResolution(HttpContext context, McpApiTokenService.ResolvedMcpApiToken? resolvedToken) =>
        context.Items[ResolutionResultKey] = new ResolutionResult(resolvedToken);

    public static bool TryGetStoredResolution(
        HttpContext context,
        out McpApiTokenService.ResolvedMcpApiToken? resolvedToken)
    {
        if (context.Items.TryGetValue(ResolutionResultKey, out object? value)
            && value is ResolutionResult result)
        {
            resolvedToken = result.Token;
            return true;
        }

        resolvedToken = null;
        return false;
    }
}
