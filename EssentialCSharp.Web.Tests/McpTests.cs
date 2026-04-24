using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

[NotInParallel("McpTests")]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class McpTests(WebApplicationFactory factory)
{
    [Test]
    public async Task McpTokenEndpoint_WithoutAuth_Returns401()
    {
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using HttpResponseMessage response = await client.PostAsync("/api/McpToken", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task McpEndpoint_WithoutToken_Returns401()
    {
        HttpClient client = factory.CreateClient();

        var request = CreateMcpInitializeRequest("/mcp");
        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task McpEndpoint_WithValidToken_Returns200AndListsTools()
    {
        // Seed a minimal user row to satisfy the FK on McpApiToken.UserId, then
        // create an opaque token via McpApiTokenService (replaces old JWT path).
        string testUserId = Guid.NewGuid().ToString();
        string rawToken;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
            db.Users.Add(new EssentialCSharpWebUser
            {
                Id = testUserId,
                UserName = "mcp-testuser",
                NormalizedUserName = "MCP-TESTUSER",
                Email = "mcp-test@example.com",
                NormalizedEmail = "MCP-TEST@EXAMPLE.COM",
                SecurityStamp = Guid.NewGuid().ToString(),
            });
            await db.SaveChangesAsync();

            var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
            (rawToken, _) = await tokenService.CreateTokenAsync(testUserId, "integration-test");
        }

        HttpClient client = factory.CreateClient();

        // Step 1: Initialize the MCP session
        var initRequest = CreateMcpInitializeRequest("/mcp");
        initRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);

        using HttpResponseMessage initResponse = await client.SendAsync(initRequest);
        await Assert.That(initResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Session ID is optional in stateless transport mode
        string? sessionId = null;
        if (initResponse.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? sessionIdValues))
            sessionId = sessionIdValues.First();

        // Step 2: List tools
        var listToolsRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""",
                Encoding.UTF8, "application/json")
        };
        listToolsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        listToolsRequest.Headers.Accept.ParseAdd("application/json");
        listToolsRequest.Headers.Accept.ParseAdd("text/event-stream");
        if (sessionId is not null)
            listToolsRequest.Headers.Add("Mcp-Session-Id", sessionId);

        using HttpResponseMessage toolsResponse = await client.SendAsync(
            listToolsRequest, HttpCompletionOption.ResponseHeadersRead);
        await Assert.That(toolsResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Streamable HTTP response: read until we find the tool names or timeout
        using Stream stream = await toolsResponse.Content.ReadAsStreamAsync();
        using StreamReader reader = new(stream);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        var body = new StringBuilder();
        string? line;
        while ((line = await reader.ReadLineAsync(cts.Token)) is not null)
        {
            body.AppendLine(line);
            if (body.ToString().Contains("search_book_content") &&
                body.ToString().Contains("get_chapter_list"))
                break;
        }

        string bodyText = body.ToString();
        await Assert.That(bodyText).Contains("search_book_content");
        await Assert.That(bodyText).Contains("get_chapter_list");
    }

    [Test]
    public async Task McpEndpoint_WithInvalidToken_Returns401()
    {
        HttpClient client = factory.CreateClient();
        var request = CreateMcpInitializeRequest("/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "mcp_invalid_token_that_does_not_exist");
        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task McpEndpoint_WithRevokedToken_Returns401()
    {
        string testUserId = Guid.NewGuid().ToString();
        string rawToken;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
            db.Users.Add(new EssentialCSharpWebUser
            {
                Id = testUserId,
                UserName = $"revoked-user-{testUserId[..8]}",
                NormalizedUserName = $"REVOKED-USER-{testUserId[..8].ToUpperInvariant()}",
                Email = $"revoked-{testUserId[..8]}@example.com",
                NormalizedEmail = $"REVOKED-{testUserId[..8].ToUpperInvariant()}@EXAMPLE.COM",
                SecurityStamp = Guid.NewGuid().ToString(),
            });
            await db.SaveChangesAsync();

            var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
            (rawToken, var entity) = await tokenService.CreateTokenAsync(testUserId, "revoke-test");
            await tokenService.RevokeTokenAsync(entity.Id, testUserId);
        }

        HttpClient client = factory.CreateClient();
        var request = CreateMcpInitializeRequest("/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task McpEndpoint_WithExpiredToken_Returns401()
    {
        string testUserId = Guid.NewGuid().ToString();
        string rawToken;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
            db.Users.Add(new EssentialCSharpWebUser
            {
                Id = testUserId,
                UserName = $"expired-user-{testUserId[..8]}",
                NormalizedUserName = $"EXPIRED-USER-{testUserId[..8].ToUpperInvariant()}",
                Email = $"expired-{testUserId[..8]}@example.com",
                NormalizedEmail = $"EXPIRED-{testUserId[..8].ToUpperInvariant()}@EXAMPLE.COM",
                SecurityStamp = Guid.NewGuid().ToString(),
            });
            await db.SaveChangesAsync();

            var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
            // Create with an expiry in the past (1 second ago)
            DateTime pastExpiry = DateTime.UtcNow.AddSeconds(-1);
            (rawToken, _) = await tokenService.CreateTokenAsync(testUserId, "expired-test", pastExpiry);
        }

        HttpClient client = factory.CreateClient();
        var request = CreateMcpInitializeRequest("/mcp");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    private static HttpRequestMessage CreateMcpInitializeRequest(string path)
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
        // MCP Streamable HTTP transport requires both content types in Accept
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }
}