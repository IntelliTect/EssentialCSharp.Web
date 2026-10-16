using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModelContextProtocol.Client;
using OpenAI.Responses;

namespace EssentialCSharp.Web.Tests;

public class ChatModerationTests : IntegrationTestBase
{
    private const string HCaptchaTestToken = "10000000-aaaa-bbbb-cccc-000000000001";
    private const string RawProviderDetail = "raw-provider-policy-details";

    [Test]
    public async Task SendMessage_WhenContentIsBlocked_ReturnsGenericUnprocessableEntity()
    {
        var (factory, client, cookieName, cookieValue) =
            await CreateAuthenticatedClientAsync(new ModeratedChatService(StreamBlockPoint.BeforeOutput));
        using var factoryScope = factory;
        using var clientScope = client;

        using HttpRequestMessage request = CreateRequest("/api/chat/message", cookieName, cookieValue);
        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.GetProperty("errorCode").GetString()).IsEqualTo("content_filtered");
        await Assert.That(payload.RootElement.GetProperty("error").GetString()).Contains("content safety");
        await Assert.That(payload.RootElement.GetRawText()).DoesNotContain(RawProviderDetail);
    }

    [Test]
    public async Task StreamMessage_WhenPromptIsBlocked_ReturnsGenericJsonError()
    {
        var (factory, client, cookieName, cookieValue) =
            await CreateAuthenticatedClientAsync(new ModeratedChatService(StreamBlockPoint.BeforeOutput));
        using var factoryScope = factory;
        using var clientScope = client;

        using HttpRequestMessage request = CreateRequest("/api/chat/stream", cookieName, cookieValue);
        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.UnprocessableEntity);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/json");
        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.GetProperty("errorCode").GetString()).IsEqualTo("content_filtered");
        await Assert.That(payload.RootElement.GetRawText()).DoesNotContain(RawProviderDetail);
    }

    [Test]
    public async Task StreamMessage_WhenCompletionIsBlocked_EmitsGenericSseError()
    {
        var (factory, client, cookieName, cookieValue) =
            await CreateAuthenticatedClientAsync(new ModeratedChatService(StreamBlockPoint.AfterSafeText));
        using var factoryScope = factory;
        using var clientScope = client;

        using HttpRequestMessage request = CreateRequest("/api/chat/stream", cookieName, cookieValue);
        using HttpResponseMessage response = await client.SendAsync(request);
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("text/event-stream");
        await Assert.That(body).Contains("safe partial text");
        await Assert.That(body).Contains("\"errorCode\":\"content_filtered\"");
        await Assert.That(body).DoesNotContain(RawProviderDetail);
        await Assert.That(body).DoesNotContain("[DONE]");
    }

    private async Task<(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Factory, HttpClient Client, string CookieName, string CookieValue)> CreateAuthenticatedClientAsync(
        IChatCompletionService chatService)
    {
        var factory = Factory.Inner.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IChatCompletionService>();
                services.AddSingleton(chatService);
            }));
        HttpClient client = McpTestHelper.CreateClient(factory);

        string userId = await McpTestHelper.CreateUserAsync(factory.Services, $"chat-moderation-{Guid.NewGuid():N}");
        (string cookieName, string cookieValue) =
            await McpTestHelper.CreateIdentityApplicationCookieAsync(factory.Services, userId);

        return (factory, client, cookieName, cookieValue);
    }

    private static HttpRequestMessage CreateRequest(string endpoint, string cookieName, string cookieValue)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new
            {
                message = "Please explain a C# topic.",
                enableContextualSearch = false,
                captchaResponse = HCaptchaTestToken
            })
        };
        McpTestHelper.AddCookie(request, cookieName, cookieValue);
        return request;
    }

    private enum StreamBlockPoint
    {
        BeforeOutput,
        AfterSafeText
    }

    private sealed class ModeratedChatService(StreamBlockPoint streamBlockPoint) : IChatCompletionService
    {
        public bool IsAvailable => true;
        public bool SupportsContextualSearch => true;

        public Task<(string response, string responseId)> GetChatCompletion(
            string prompt,
            string? systemPrompt = null,
            string? previousResponseId = null,
            McpClient? mcpClient = null,
#pragma warning disable OPENAI001
            IEnumerable<ResponseTool>? tools = null,
            ResponseReasoningEffortLevel? reasoningEffortLevel = null,
#pragma warning restore OPENAI001
            bool enableContextualSearch = false,
            string? endUserId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromException<(string response, string responseId)>(CreateContentFilteredException());

        public IAsyncEnumerable<(string text, string? responseId)> GetChatCompletionStream(
            string prompt,
            string? systemPrompt = null,
            string? previousResponseId = null,
            McpClient? mcpClient = null,
#pragma warning disable OPENAI001
            IEnumerable<ResponseTool>? tools = null,
            ResponseReasoningEffortLevel? reasoningEffortLevel = null,
#pragma warning restore OPENAI001
            bool enableContextualSearch = false,
            string? endUserId = null,
            CancellationToken cancellationToken = default) =>
            streamBlockPoint == StreamBlockPoint.AfterSafeText ? FailAfterSafeText() : FailBeforeOutput();

        private static async IAsyncEnumerable<(string text, string? responseId)> FailBeforeOutput()
        {
            await Task.Yield();
            await Task.FromException(CreateContentFilteredException());
            yield break;
        }

        private static async IAsyncEnumerable<(string text, string? responseId)> FailAfterSafeText()
        {
            yield return ("safe partial text", null);
            await Task.Yield();
            await Task.FromException(CreateContentFilteredException());
            yield break;
        }

        private static ChatContentFilteredException CreateContentFilteredException() =>
            new(new InvalidOperationException(RawProviderDetail));
    }
}
