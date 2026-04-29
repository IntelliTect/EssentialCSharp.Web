using System.Net;
using System.Text;
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
        HttpClient client = McpTestHelper.CreateClient(factory);

        using HttpResponseMessage response = await client.PostAsync("/api/McpToken", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task McpEndpoint_WithoutToken_Returns401()
    {
        HttpClient client = McpTestHelper.CreateClient(factory);

        using var request = McpTestHelper.CreateInitializeRequest("/mcp");
        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task McpEndpoint_WithValidToken_Returns200AndListsTools()
    {
        (_, string rawToken) = await McpTestHelper.CreateUserAndTokenAsync(
            factory,
            "integration-test",
            userPrefix: "mcp-testuser");

        HttpClient client = McpTestHelper.CreateClient(factory);

        // Step 1: Initialize the MCP session
        using var initRequest = McpTestHelper.CreateInitializeRequest("/mcp");
        McpTestHelper.AddBearerToken(initRequest, rawToken);

        using HttpResponseMessage initResponse = await client.SendAsync(initRequest);
        await Assert.That(initResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Session ID is optional in stateless transport mode
        string? sessionId = null;
        if (initResponse.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? sessionIdValues))
            sessionId = sessionIdValues.First();

        // Step 2: List tools
        using var listToolsRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""",
                Encoding.UTF8, "application/json")
        };
        McpTestHelper.AddBearerToken(listToolsRequest, rawToken);
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
        HttpClient client = McpTestHelper.CreateClient(factory);
        using var request = McpTestHelper.CreateInitializeRequest("/mcp");
        McpTestHelper.AddBearerToken(request, "mcp_invalid_token_that_does_not_exist");
        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task McpEndpoint_WithRevokedToken_Returns401()
    {
        string testUserId = await McpTestHelper.CreateUserAsync(factory, "revoked-user");
        string rawToken;
        using (var scope = factory.Services.CreateScope())
        {
            var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
            (rawToken, var entity) = await tokenService.CreateTokenAsync(testUserId, "revoke-test");
            await tokenService.RevokeTokenAsync(entity.Id, testUserId);
        }

        HttpClient client = McpTestHelper.CreateClient(factory);
        using var request = McpTestHelper.CreateInitializeRequest("/mcp");
        McpTestHelper.AddBearerToken(request, rawToken);
        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task McpEndpoint_WithExpiredToken_Returns401()
    {
        (_, string rawToken) = await McpTestHelper.CreateUserAndTokenAsync(
            factory,
            "expired-test",
            userPrefix: "expired-user",
            expiresAt: DateTime.UtcNow.AddSeconds(-1));

        HttpClient client = McpTestHelper.CreateClient(factory);
        using var request = McpTestHelper.CreateInitializeRequest("/mcp");
        McpTestHelper.AddBearerToken(request, rawToken);
        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task McpEndpoint_PreflightFromLoopbackOrigin_ReturnsCorsHeaders()
    {
        HttpClient client = McpTestHelper.CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Options, "/mcp");
        request.Headers.Add("Origin", "http://localhost:6274");
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        await Assert.That(response.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? origins)).IsTrue();
        await Assert.That(origins?.SingleOrDefault()).IsEqualTo("http://localhost:6274");
        await Assert.That(response.Headers.TryGetValues("Access-Control-Allow-Methods", out IEnumerable<string>? methods)).IsTrue();
        await Assert.That(methods?.SingleOrDefault()).Contains("POST");
        await Assert.That(response.Headers.TryGetValues("Access-Control-Allow-Headers", out IEnumerable<string>? headers)).IsTrue();
        await Assert.That(headers?.SingleOrDefault()).Contains("authorization");
    }

    [Test]
    public async Task McpEndpoint_GetFromLoopbackOrigin_Returns405WithoutRedirect()
    {
        HttpClient client = McpTestHelper.CreateClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        request.Headers.Add("Origin", "http://localhost:6274");
        request.Headers.Accept.ParseAdd("text/event-stream");

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.MethodNotAllowed);
        await Assert.That(response.Headers.Location).IsNull();
        await Assert.That(response.Headers.TryGetValues("Access-Control-Allow-Origin", out IEnumerable<string>? origins)).IsTrue();
        await Assert.That(origins?.SingleOrDefault()).IsEqualTo("http://localhost:6274");
    }
}
