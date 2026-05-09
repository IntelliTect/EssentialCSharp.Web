using System.Net.Http.Headers;
using System.Text;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

internal static class McpTestHelper
{
    public static HttpClient CreateClient(WebApplicationFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false
    });

    public static HttpRequestMessage CreateInitializeRequest(string path = "/mcp")
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new StringContent(
                """
                {
                    "jsonrpc": "2.0",
                    "id": 1,
                    "method": "initialize",
                    "params": {
                        "protocolVersion": "2024-11-05",
                        "capabilities": {},
                        "clientInfo": { "name": "test-client", "version": "1.0" }
                    }
                }
                """,
                Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }

    public static void AddBearerToken(HttpRequestMessage request, string rawToken) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);

    public static void AddCookie(HttpRequestMessage request, string cookieName, string cookieValue) =>
        request.Headers.Add("Cookie", $"{cookieName}={cookieValue}");

    public static async Task<string> CreateUserAsync(WebApplicationFactory factory, string userPrefix)
    {
        string userId = Guid.NewGuid().ToString();
        string suffix = Guid.NewGuid().ToString("N")[..8];
        string userName = $"{userPrefix.ToLowerInvariant()}-{suffix}";

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
        db.Users.Add(new EssentialCSharpWebUser
        {
            Id = userId,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            Email = $"{userName}@example.com",
            NormalizedEmail = $"{userName.ToUpperInvariant()}@EXAMPLE.COM",
            SecurityStamp = Guid.NewGuid().ToString(),
        });
        await db.SaveChangesAsync();

        return userId;
    }

    public static async Task<(string UserId, string RawToken)> CreateUserAndTokenAsync(
        WebApplicationFactory factory,
        string tokenName,
        string userPrefix = "mcp-test",
        DateTime? expiresAt = null)
    {
        string userId = await CreateUserAsync(factory, userPrefix);

        using var scope = factory.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
        (string rawToken, _) = expiresAt is { } expiry
            ? await tokenService.CreateTokenAsync(userId, tokenName, expiry)
            : await tokenService.CreateTokenAsync(userId, tokenName);

        return (userId, rawToken);
    }

    public static async Task<(string CookieName, string CookieValue)> CreateIdentityApplicationCookieAsync(
        WebApplicationFactory factory,
        string userId)
    {
        using var scope = factory.Services.CreateScope();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<EssentialCSharpWebUser>>();
        EssentialCSharpWebUser user = await signInManager.UserManager.FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"Could not find test user '{userId}' to create an identity cookie.");
        var principal = await signInManager.CreateUserPrincipalAsync(user);

        CookieAuthenticationOptions cookieOptions = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        string cookieName = cookieOptions.Cookie.Name
            ?? throw new InvalidOperationException("Identity application cookie name is not configured.");

        var ticket = new AuthenticationTicket(
            principal,
            new AuthenticationProperties
            {
                IssuedUtc = DateTimeOffset.UtcNow,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(cookieOptions.ExpireTimeSpan),
            },
            IdentityConstants.ApplicationScheme);

        string cookieValue = cookieOptions.TicketDataFormat.Protect(ticket)
            ?? throw new InvalidOperationException("Failed to protect the identity application ticket.");

        return (cookieName, cookieValue);
    }
}
