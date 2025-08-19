using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using Xunit;

namespace EssentialCSharp.Web.Tests;

public class McpSdkIntegrationTests : IClassFixture<WebApplicationFactory>
{
    private readonly WebApplicationFactory _factory;
    public McpSdkIntegrationTests(WebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ListTools_Returns_GetEcsharpContext()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var endpoint = new Uri(client.BaseAddress!, "/mcp");
        var transportOptions = new SseClientTransportOptions
        {
            Endpoint = endpoint,
            Name = "test-client",
            TransportMode = HttpTransportMode.StreamableHttp
        };

        await using var mcpClient = await McpClientFactory.CreateAsync(new SseClientTransport(transportOptions, client));
        var tools = await mcpClient.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "get_ecsharp_context");
    }
}
