using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EssentialCSharp.Web.AppHost.Tests.Journeys;

/// <summary>
/// An in-process stand-in for an OpenAI-compatible model (<c>POST /v1/chat/completions</c>), which the web app's
/// local chat backend calls. Journeys get a deterministic reply, and can make the model fail on demand, without
/// Docker images, model downloads, or Azure credentials.
/// </summary>
public sealed class FakeChatModelServer : IAsyncDisposable
{
    /// <summary>The reply returned for every prompt that does not contain <see cref="FailTrigger"/>.</summary>
    public const string CannedReply = "Fake model reply: a class is a template for creating objects.";

    /// <summary>Prompts containing this text get an HTTP 500, to exercise the chat error path.</summary>
    public const string FailTrigger = "[fake-model:fail]";

    private readonly WebApplication _app;

    private FakeChatModelServer(WebApplication app, Uri endpoint, ConcurrentQueue<string> receivedPrompts)
    {
        _app = app;
        Endpoint = endpoint;
        ReceivedPrompts = receivedPrompts;
    }

    /// <summary>Base address the web app should use as <c>AIOptions:LocalEndpoint</c>.</summary>
    public Uri Endpoint { get; }

    /// <summary>User prompts received, most recent last.</summary>
    public ConcurrentQueue<string> ReceivedPrompts { get; }

    public static async Task<FakeChatModelServer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");

        WebApplication app = builder.Build();
        var receivedPrompts = new ConcurrentQueue<string>();
        app.MapPost("/v1/chat/completions", (HttpRequest request) => HandleCompletionAsync(request, receivedPrompts));
        await app.StartAsync();

        string address = app.Services.GetRequiredService<IServer>().Features.GetRequiredFeature<IServerAddressesFeature>().Addresses.Single();
        return new FakeChatModelServer(app, new Uri(address), receivedPrompts);
    }

    private static async Task<IResult> HandleCompletionAsync(HttpRequest request, ConcurrentQueue<string> receivedPrompts)
    {
        JsonNode? body = await JsonNode.ParseAsync(request.Body);
        string prompt = body?["messages"]?.AsArray()
            .LastOrDefault(message => message?["role"]?.GetValue<string>() == "user")?["content"]?.GetValue<string>()
            ?? string.Empty;
        receivedPrompts.Enqueue(prompt);

        if (prompt.Contains(FailTrigger, StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status500InternalServerError);
        }

        string json = JsonSerializer.Serialize(new
        {
            id = $"chatcmpl-{Guid.NewGuid():N}",
            @object = "chat.completion",
            choices = new[] { new { index = 0, message = new { role = "assistant", content = CannedReply }, finish_reason = "stop" } },
        });
        return Results.Text(json, "application/json");
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
