using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

[NotInParallel("McpTests")]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class McpToolContractTests(WebApplicationFactory factory)
{
    [Test]
    public async Task McpToolsList_StructuredAndHybridTools_AdvertiseOutputSchema()
    {
        (HttpClient client, string rawToken, string? sessionId) = await CreateAuthenticatedSessionAsync();

        using HttpResponseMessage response = await SendRpcAsync(
            client,
            rawToken,
            sessionId,
            """
            {"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
            """);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using JsonDocument document = JsonDocument.Parse(await ReadMcpPayloadAsync(response));
        JsonElement tools = document.RootElement.GetProperty("result").GetProperty("tools");

        await Assert.That(GetTool(tools, "get_chapter_list").TryGetProperty("outputSchema", out _)).IsTrue();
        await Assert.That(GetTool(tools, "get_chapter_sections").TryGetProperty("outputSchema", out _)).IsTrue();
        await Assert.That(GetTool(tools, "get_direct_content_url").TryGetProperty("outputSchema", out _)).IsTrue();
        await Assert.That(GetTool(tools, "get_navigation_context").TryGetProperty("outputSchema", out _)).IsTrue();
        await Assert.That(GetTool(tools, "get_chapter_summary").TryGetProperty("outputSchema", out _)).IsTrue();
        await Assert.That(GetTool(tools, "search_listings_by_code").TryGetProperty("outputSchema", out _)).IsTrue();
        await Assert.That(GetTool(tools, "find_book_help_for_diagnostic").TryGetProperty("outputSchema", out _)).IsTrue();

        await Assert.That(GetTool(tools, "get_section_content").TryGetProperty("outputSchema", out _)).IsFalse();
        await Assert.That(GetTool(tools, "get_listing_source_code").TryGetProperty("outputSchema", out _)).IsFalse();
    }

    [Test]
    public async Task McpCall_GetChapterSections_ReturnsStructuredContentAndJsonTextFallback()
    {
        (HttpClient client, string rawToken, string? sessionId) = await CreateAuthenticatedSessionAsync();

        using HttpResponseMessage response = await SendRpcAsync(
            client,
            rawToken,
            sessionId,
            """
            {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"get_chapter_sections","arguments":{"chapter":1}}}
            """);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using JsonDocument document = JsonDocument.Parse(await ReadMcpPayloadAsync(response));
        JsonElement result = document.RootElement.GetProperty("result");
        JsonElement structuredContent = result.GetProperty("structuredContent");

        await Assert.That(structuredContent.GetProperty("chapterNumber").GetInt32()).IsEqualTo(1);
        await Assert.That(structuredContent.GetProperty("chapterTitle").GetString()).IsNotNull();
        await Assert.That(structuredContent.GetProperty("sections").GetArrayLength()).IsGreaterThan(0);

        JsonElement firstSection = structuredContent.GetProperty("sections")[0];
        await Assert.That(firstSection.GetProperty("key").GetString()).IsNotNull();
        await Assert.That(firstSection.GetProperty("href").GetString()).StartsWith("/");
        await Assert.That(firstSection.GetProperty("url").GetString()).StartsWith(GetConfiguredBaseUrl());

        string text = result.GetProperty("content")[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Expected JSON text fallback for structured MCP tool result.");
        using JsonDocument textDocument = JsonDocument.Parse(text);
        await Assert.That(textDocument.RootElement.GetProperty("chapterNumber").GetInt32()).IsEqualTo(1);
        await Assert.That(textDocument.RootElement.GetProperty("sections").GetArrayLength()).IsGreaterThan(0);
    }

    [Test]
    public async Task McpCall_GetSectionContent_IncludesTableRowsInTextOutput()
    {
        (HttpClient client, string rawToken, string? sessionId) = await CreateAuthenticatedSessionAsync();

        using HttpResponseMessage response = await SendRpcAsync(
            client,
            rawToken,
            sessionId,
            """
            {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"get_section_content","arguments":{"sectionKey":"c-keywords"}}}
            """);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using JsonDocument document = JsonDocument.Parse(await ReadMcpPayloadAsync(response));
        JsonElement result = document.RootElement.GetProperty("result");
        string text = result.GetProperty("content")[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Expected text content for get_section_content.");

        await Assert.That(text).Contains("Table 1.1: C# Keywords");
        await Assert.That(text).Contains("| abstract | add*(1) | alias*(2) | and* |");
    }

    [Test]
    public async Task McpCall_SearchListingsByCode_ReturnsReadableTextAndStructuredContent()
    {
        (HttpClient client, string rawToken, string? sessionId) = await CreateAuthenticatedSessionAsync();

        using HttpResponseMessage response = await SendRpcAsync(
            client,
            rawToken,
            sessionId,
            """
            {"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"search_listings_by_code","arguments":{"pattern":"Main("}}}
            """);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using JsonDocument document = JsonDocument.Parse(await ReadMcpPayloadAsync(response));
        JsonElement result = document.RootElement.GetProperty("result");
        JsonElement structuredContent = result.GetProperty("structuredContent");

        await Assert.That(structuredContent.GetProperty("pattern").GetString()).IsEqualTo("Main(");
        JsonElement matches = structuredContent.GetProperty("matches");
        await Assert.That(matches.GetArrayLength()).IsGreaterThan(0);
        await Assert.That(matches[0].GetProperty("chapterNumber").GetInt32()).IsGreaterThan(0);
        await Assert.That(matches[0].GetProperty("content").GetString()).IsNotNull();

        JsonElement content = result.GetProperty("content");
        await Assert.That(content.GetArrayLength()).IsEqualTo(2);

        string readableText = content[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Expected readable text content for search_listings_by_code.");
        await Assert.That(readableText).Contains("Listings Containing 'Main('");

        string jsonText = content[1].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Expected JSON fallback text for search_listings_by_code.");
        using JsonDocument jsonTextDocument = JsonDocument.Parse(jsonText);
        await Assert.That(jsonTextDocument.RootElement.GetProperty("matches").GetArrayLength()).IsGreaterThan(0);
    }

    [Test]
    public async Task McpCall_FindBookHelpForDiagnostic_ReturnsReadableTextAndStructuredContent()
    {
        (HttpClient client, string rawToken, string? sessionId) = await CreateAuthenticatedSessionAsync();

        using HttpResponseMessage response = await SendRpcAsync(
            client,
            rawToken,
            sessionId,
            """
            {"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"find_book_help_for_diagnostic","arguments":{"diagnostic":"exceptions"}}}
            """);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        using JsonDocument document = JsonDocument.Parse(await ReadMcpPayloadAsync(response));
        JsonElement result = document.RootElement.GetProperty("result");
        JsonElement structuredContent = result.GetProperty("structuredContent");

        await Assert.That(structuredContent.GetProperty("diagnostic").GetString()).IsEqualTo("exceptions");

        int totalMatches =
            structuredContent.GetProperty("relevantSections").GetArrayLength() +
            structuredContent.GetProperty("relevantBookContent").GetArrayLength() +
            structuredContent.GetProperty("relatedGuidelines").GetArrayLength();
        await Assert.That(totalMatches).IsGreaterThan(0);

        JsonElement content = result.GetProperty("content");
        await Assert.That(content.GetArrayLength()).IsEqualTo(2);

        string readableText = content[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Expected readable text content for find_book_help_for_diagnostic.");
        await Assert.That(readableText).Contains("# Book Help for: exceptions");

        string jsonText = content[1].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Expected JSON fallback text for find_book_help_for_diagnostic.");
        using JsonDocument jsonTextDocument = JsonDocument.Parse(jsonText);
        await Assert.That(jsonTextDocument.RootElement.GetProperty("diagnostic").GetString()).IsEqualTo("exceptions");
    }

    [Test]
    public async Task McpCall_GetChapterSections_WithInvalidChapter_ReturnsMcpError()
    {
        (HttpClient client, string rawToken, string? sessionId) = await CreateAuthenticatedSessionAsync();

        using HttpResponseMessage response = await SendRpcAsync(
            client,
            rawToken,
            sessionId,
            """
            {"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"get_chapter_sections","arguments":{"chapter":999}}}
            """);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        string payload = await ReadMcpPayloadAsync(response);
        await Assert.That(payload).Contains("Chapter 999 not found. Use GetChapterList to see all available chapters.");
    }

    private async Task<(HttpClient Client, string RawToken, string? SessionId)> CreateAuthenticatedSessionAsync()
    {
        string rawToken = await CreateTokenAsync();
        HttpClient client = factory.CreateClient();

        using var initRequest = CreateMcpInitializeRequest("/mcp");
        initRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);

        using HttpResponseMessage initResponse = await client.SendAsync(initRequest);
        await Assert.That(initResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        string? sessionId = null;
        if (initResponse.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? sessionIdValues))
        {
            sessionId = sessionIdValues.First();
        }

        return (client, rawToken, sessionId);
    }

    private async Task<string> CreateTokenAsync()
    {
        string testUserId = Guid.NewGuid().ToString();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
        db.Users.Add(new EssentialCSharpWebUser
        {
            Id = testUserId,
            UserName = $"mcp-contract-{testUserId[..8]}",
            NormalizedUserName = $"MCP-CONTRACT-{testUserId[..8].ToUpperInvariant()}",
            Email = $"mcp-contract-{testUserId[..8]}@example.com",
            NormalizedEmail = $"MCP-CONTRACT-{testUserId[..8].ToUpperInvariant()}@EXAMPLE.COM",
            SecurityStamp = Guid.NewGuid().ToString(),
        });
        await db.SaveChangesAsync();

        var tokenService = scope.ServiceProvider.GetRequiredService<McpApiTokenService>();
        (string rawToken, _) = await tokenService.CreateTokenAsync(testUserId, "mcp-contract-test");
        return rawToken;
    }

    private string GetConfiguredBaseUrl()
    {
        string baseUrl = factory.Services.GetRequiredService<IOptions<SiteSettings>>().Value.BaseUrl;
        return baseUrl.TrimEnd('/') + "/";
    }

    private static async Task<HttpResponseMessage> SendRpcAsync(
        HttpClient client,
        string rawToken,
        string? sessionId,
        string payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (sessionId is not null)
        {
            request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    private static async Task<string> ReadMcpPayloadAsync(HttpResponseMessage response)
    {
        using Stream stream = await response.Content.ReadAsStreamAsync();
        using StreamReader reader = new(stream);

        List<string> lines = [];
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line.Trim());
            }
        }

        List<string> dataPayloads = lines
            .Where(line => line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            .Select(line => line["data:".Length..].Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (dataPayloads.Count > 0)
        {
            return dataPayloads.LastOrDefault(line => line.StartsWith('{'))
                ?? dataPayloads[^1];
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static JsonElement GetTool(JsonElement tools, string toolName)
    {
        foreach (JsonElement tool in tools.EnumerateArray())
        {
            if (string.Equals(tool.GetProperty("name").GetString(), toolName, StringComparison.Ordinal))
            {
                return tool;
            }
        }

        throw new InvalidOperationException($"Could not find MCP tool '{toolName}' in tools/list response.");
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
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        return request;
    }
}
