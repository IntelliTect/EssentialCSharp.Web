using System.Net;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Regression tests: an MCP bearer token acts as its user only on the MCP transport (POST /mcp). Elsewhere it must
/// not spend the owner's site-wide or content rate-limit budget, or a leaked token could lock the owner out of the
/// site, including the page they would use to revoke it.
/// </summary>
public class McpBearerRateLimitIsolationTests : IntegrationTestBase
{
    private const int GlobalPermitLimit = 30;

    private static async Task SendBearerGetsAsync(HttpClient client, string path, string rawToken, int count)
    {
        for (int i = 0; i < count; i++)
        {
            using HttpRequestMessage request = new(HttpMethod.Get, path);
            McpTestHelper.AddBearerToken(request, rawToken);
            request.Headers.Accept.ParseAdd("text/event-stream");
            using HttpResponseMessage response = await client.SendAsync(request);
        }
    }

    private static async Task<HttpStatusCode> SendWithCookieAsync(HttpClient client, HttpMethod method, string path, (string Name, string Value) cookie)
    {
        using HttpRequestMessage request = new(method, path);
        McpTestHelper.AddCookie(request, cookie.Name, cookie.Value);
        using HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Test]
    public async Task BearerRequestsToTheGetShim_DoNotSpendTheOwnersSiteBucket()
    {
        (string userId, string rawToken) = await McpTestHelper.CreateUserAndTokenAsync(Factory, "isolation", userPrefix: "mcp-isolation");
        (string Name, string Value) ownerCookie = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);
        HttpClient client = McpTestHelper.CreateClient(Factory);

        await SendBearerGetsAsync(client, "/mcp", rawToken, GlobalPermitLimit + 1);
        HttpStatusCode ownerPage = await SendWithCookieAsync(client, HttpMethod.Get, "/Identity/Account/Manage/McpAccess", ownerCookie);

        await Assert.That(ownerPage).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task BearerRequestsToOtherMcpPaths_DoNotSpendTheOwnersContentBucket()
    {
        (string userId, string rawToken) = await McpTestHelper.CreateUserAndTokenAsync(Factory, "isolation", userPrefix: "mcp-isolation");
        (string Name, string Value) ownerCookie = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);
        HttpClient client = McpTestHelper.CreateClient(Factory);

        await SendBearerGetsAsync(client, "/mcp/anything", rawToken, 20);
        HttpStatusCode ownerContent = await SendWithCookieAsync(client, HttpMethod.Get, "/hello-world", ownerCookie);

        await Assert.That(ownerContent).IsNotEqualTo(HttpStatusCode.TooManyRequests);
    }

    [Test]
    public async Task OwnerCanStillRevokeTheTokenAfterABearerBurst()
    {
        (string userId, string rawToken) = await McpTestHelper.CreateUserAndTokenAsync(Factory, "isolation", userPrefix: "mcp-isolation");
        (string Name, string Value) ownerCookie = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);
        Guid tokenId = InServiceScope(services =>
        {
            byte[] hash = McpApiTokenService.HashToken(rawToken);
            return services.GetRequiredService<EssentialCSharpWebContext>().McpApiTokens.Single(t => t.TokenHash == hash).Id;
        });
        HttpClient client = McpTestHelper.CreateClient(Factory);

        await SendBearerGetsAsync(client, "/mcp", rawToken, GlobalPermitLimit + 1);
        HttpStatusCode revoke = await SendWithCookieAsync(client, HttpMethod.Delete, $"/api/McpToken/{tokenId}", ownerCookie);

        await Assert.That(revoke).IsEqualTo(HttpStatusCode.NoContent);
    }
}
