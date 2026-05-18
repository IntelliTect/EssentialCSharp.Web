using System.Net;
using System.Text;
using System.Text.Json;
using EssentialCSharp.Chat;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace EssentialCSharp.Chat.Tests;

public class LocalChatServiceTests
{
    [Test]
    public async Task GetChatCompletion_BuildsExpectedRequest()
    {
        var requests = new List<HttpRequestMessage>();
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(CreateJsonResponse("""
            {"id":"resp-1","choices":[{"message":{"content":"hello back"}}]}
            """));

        var (service, client) = CreateService(new RecordingHttpMessageHandler(requests, responses));
        using var clientScope = client;
        using var serviceScope = service;

        var (response, responseId) = await service.GetChatCompletion("hello");

        await Assert.That(response).IsEqualTo("hello back");
        await Assert.That(responseId).IsEqualTo("resp-1");
        await Assert.That(requests.Count).IsEqualTo(1);

        var request = requests[0];
        await Assert.That(request.RequestUri!.AbsolutePath).IsEqualTo("/v1/chat/completions");
        await Assert.That(request.Headers.Authorization?.Scheme).IsEqualTo("Bearer");
        await Assert.That(request.Headers.Authorization?.Parameter).IsEqualTo("local-dev-key");

        string requestBody = await request.Content!.ReadAsStringAsync();
        using var payload = JsonDocument.Parse(requestBody);
        await Assert.That(payload.RootElement.GetProperty("model").GetString()).IsEqualTo("qwen2.5-coder:7b");
        await Assert.That(payload.RootElement.GetProperty("stream").GetBoolean()).IsFalse();

        var messages = payload.RootElement.GetProperty("messages");
        await Assert.That(messages.GetArrayLength()).IsEqualTo(2);
        await Assert.That(messages[0].GetProperty("role").GetString()).IsEqualTo("system");
        await Assert.That(messages[0].GetProperty("content").GetString()).IsEqualTo("system prompt");
        await Assert.That(messages[1].GetProperty("role").GetString()).IsEqualTo("user");
        await Assert.That(messages[1].GetProperty("content").GetString()).IsEqualTo("hello");
    }

    [Test]
    public async Task LocalChatService_DoesNotSupportContextualSearch()
    {
        var (service, client) = CreateService(new RecordingHttpMessageHandler([], []));
        using var clientScope = client;
        using var serviceScope = service;

        await Assert.That(service.SupportsContextualSearch).IsFalse();
        await Assert.ThrowsAsync<NotSupportedException>(() => service.GetChatCompletion("hello", enableContextualSearch: true));
    }

    [Test]
    [Arguments("non-success")]
    [Arguments("invalid-payload")]
    public async Task GetChatCompletion_WhenResponseIsInvalid_ThrowsChatBackendUnavailableException(string caseName)
    {
        var requests = new List<HttpRequestMessage>();
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(CreateFailureResponse(caseName));

        var (service, client) = CreateService(new RecordingHttpMessageHandler(requests, responses));
        using var clientScope = client;
        using var serviceScope = service;

        await Assert.ThrowsAsync<ChatBackendUnavailableException>(() => service.GetChatCompletion("hello"));
    }

    [Test]
    public async Task GetChatCompletion_ReusesConversationHistory_WhenPreviousResponseIdProvided()
    {
        var requests = new List<HttpRequestMessage>();
        var responses = new Queue<HttpResponseMessage>();
        responses.Enqueue(CreateJsonResponse("""
            {"id":"resp-1","choices":[{"message":{"content":"assistant one"}}]}
            """));
        responses.Enqueue(CreateJsonResponse("""
            {"id":"resp-2","choices":[{"message":{"content":"assistant two"}}]}
            """));

        var (service, client) = CreateService(new RecordingHttpMessageHandler(requests, responses));
        using var clientScope = client;
        using var serviceScope = service;

        var first = await service.GetChatCompletion("first");
        _ = await service.GetChatCompletion("second", previousResponseId: first.responseId);

        await Assert.That(requests.Count).IsEqualTo(2);

        string firstBody = await requests[0].Content!.ReadAsStringAsync();
        using var firstPayload = JsonDocument.Parse(firstBody);
        var firstMessages = firstPayload.RootElement.GetProperty("messages");
        await Assert.That(firstMessages.GetArrayLength()).IsEqualTo(2);

        string secondBody = await requests[1].Content!.ReadAsStringAsync();
        using var secondPayload = JsonDocument.Parse(secondBody);
        var secondMessages = secondPayload.RootElement.GetProperty("messages");
        await Assert.That(secondMessages.GetArrayLength()).IsEqualTo(4);
        await Assert.That(secondMessages[1].GetProperty("role").GetString()).IsEqualTo("user");
        await Assert.That(secondMessages[1].GetProperty("content").GetString()).IsEqualTo("first");
        await Assert.That(secondMessages[2].GetProperty("role").GetString()).IsEqualTo("assistant");
        await Assert.That(secondMessages[2].GetProperty("content").GetString()).IsEqualTo("assistant one");
        await Assert.That(secondMessages[3].GetProperty("role").GetString()).IsEqualTo("user");
        await Assert.That(secondMessages[3].GetProperty("content").GetString()).IsEqualTo("second");
    }

    private static (LocalChatService Service, HttpClient Client) CreateService(HttpMessageHandler handler)
    {
        var options = Options.Create(new AIOptions
        {
            LocalChatModel = "qwen2.5-coder:7b",
            SystemPrompt = "system prompt"
        });

        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("http://localhost:11434")
        };

        var httpClientFactory = new Mock<IHttpClientFactory>();
        httpClientFactory
            .Setup(f => f.CreateClient("LocalAIChat"))
            .Returns(client);

        var logger = Mock.Of<ILogger<LocalChatService>>();
        return (new LocalChatService(options, httpClientFactory.Object, logger), client);
    }

    private static HttpResponseMessage CreateJsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage CreateFailureResponse(string caseName) => caseName switch
    {
        "non-success" => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("upstream error", Encoding.UTF8, "text/plain")
        },
        "invalid-payload" => CreateJsonResponse("""{"id":"resp-1","choices":[]}"""),
        _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Unknown failure case.")
    };

    private sealed class RecordingHttpMessageHandler(
        List<HttpRequestMessage> requests,
        Queue<HttpResponseMessage> responses) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var snapshot = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Content = request.Content is null
                    ? null
                    : new StringContent(await request.Content.ReadAsStringAsync(cancellationToken), Encoding.UTF8, request.Content.Headers.ContentType?.MediaType)
            };
            if (request.Headers.Authorization is not null)
            {
                snapshot.Headers.Authorization = request.Headers.Authorization;
            }

            requests.Add(snapshot);
            return responses.Dequeue();
        }
    }
}
