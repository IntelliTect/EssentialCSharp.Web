using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace EssentialCSharp.Web.Tests;

public class McpTests
{
    [Fact]
    public async Task McpTokenEndpoint_WithoutAuth_Returns401()
    {
        using WebApplicationFactory factory = new();
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using HttpResponseMessage response = await client.PostAsync("/api/McpToken", null);

        // [ApiController] returns 401 directly; it does not redirect to login like Razor Pages
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_WithoutToken_Returns401()
    {
        using WebApplicationFactory factory = new();
        HttpClient client = factory.CreateClient();

        var request = CreateMcpInitializeRequest("/mcp");
        using HttpResponseMessage response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task McpEndpoint_WithValidToken_Returns200AndListsTools()
    {
        using WebApplicationFactory factory = new();

        McpTokenService? tokenService = factory.Services.GetService<McpTokenService>();
        Assert.NotNull(tokenService);

        var (token, _) = tokenService.GenerateToken("test-user-id", "testuser", "test@example.com");

        HttpClient client = factory.CreateClient();

        // Step 1: Initialize the MCP session
        var initRequest = CreateMcpInitializeRequest("/mcp");
        initRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage initResponse = await client.SendAsync(initRequest);
        Assert.Equal(HttpStatusCode.OK, initResponse.StatusCode);

        string sessionId = initResponse.Headers.GetValues("Mcp-Session-Id").First();

        // Step 2: List tools
        var listToolsRequest = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(
                """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""",
                Encoding.UTF8, "application/json")
        };
        listToolsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        listToolsRequest.Headers.Accept.ParseAdd("application/json");
        listToolsRequest.Headers.Accept.ParseAdd("text/event-stream");
        listToolsRequest.Headers.Add("Mcp-Session-Id", sessionId);

        using HttpResponseMessage toolsResponse = await client.SendAsync(
            listToolsRequest, HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(HttpStatusCode.OK, toolsResponse.StatusCode);

        // SSE streams arrive line-by-line; read until we find the data line or timeout
        using Stream stream = await toolsResponse.Content.ReadAsStreamAsync();
        using StreamReader reader = new(stream);
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(10));
        string body = "";
        string? line;
        while ((line = await reader.ReadLineAsync(cts.Token)) is not null)
        {
            body += line + "\n";
            if (body.Contains("search_book_content") && body.Contains("get_chapter_list"))
                break;
        }

        // The MCP C# SDK converts PascalCase method names to snake_case for the wire protocol
        Assert.Contains("search_book_content", body);
        Assert.Contains("get_chapter_list", body);
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