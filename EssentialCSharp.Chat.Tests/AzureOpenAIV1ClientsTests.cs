using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Azure.Core;
using EssentialCSharp.Chat.Common.Services;
using OpenAI.Responses;

namespace EssentialCSharp.Chat.Tests;

/// <summary>
/// Pins the exact requests sent to the Azure OpenAI v1 API. The live resource is private, so a wrong route
/// (for example <c>/openai/deployments/{name}/responses</c>, which 404s) is otherwise only found by a real chat.
/// </summary>
public class AzureOpenAIV1ClientsTests
{
    private const string Endpoint = "https://example.cognitiveservices.azure.com/";

    [Test]
    [Arguments("https://example.cognitiveservices.azure.com/", "https://example.cognitiveservices.azure.com/openai/v1")]
    [Arguments("https://example.cognitiveservices.azure.com", "https://example.cognitiveservices.azure.com/openai/v1")]
    public async Task GetV1Endpoint_AppendsOpenAiV1(string endpoint, string expected)
    {
        await Assert.That(AzureOpenAIV1Clients.GetV1Endpoint(endpoint).ToString()).IsEqualTo(expected);
    }

    [Test]
    public async Task ResponsesClient_PostsToV1ResponsesWithBearerTokenAndDeploymentAsModel()
    {
#pragma warning disable OPENAI001 // Responses API types are experimental in the OpenAI SDK.
        CapturingHandler handler = new();
        ResponsesClient client = AzureOpenAIV1Clients.CreateResponsesClient(Endpoint, new FakeCredential(), CreateTransport(handler));

        CreateResponseOptions options = new() { Model = "web-chat" };
        options.InputItems.Add(ResponseItem.CreateUserMessageItem("hello"));
        await Assert.That(async () => await client.CreateResponseAsync(options)).Throws<ClientResultException>();

        using (Assert.Multiple())
        {
            await Assert.That(handler.Request.Method).IsEqualTo(HttpMethod.Post);
            // Uri equality includes the query string, so this also proves no api-version is appended.
            await Assert.That(handler.Request.Uri).IsEqualTo(new Uri("https://example.cognitiveservices.azure.com/openai/v1/responses"));
            await Assert.That(handler.Request.Authorization).IsEqualTo("Bearer test-token");
            await Assert.That(JsonDocument.Parse(handler.Request.Body).RootElement.GetProperty("model").GetString()).IsEqualTo("web-chat");
        }
#pragma warning restore OPENAI001
    }

    [Test]
    public async Task EmbeddingClient_PostsToV1EmbeddingsWithBearerTokenAndDeploymentAsModel()
    {
        CapturingHandler handler = new();
        var client = AzureOpenAIV1Clients.CreateEmbeddingClient(Endpoint, "embedding-model", new FakeCredential(), CreateTransport(handler));

        await Assert.That(async () => await client.GenerateEmbeddingAsync("hello")).Throws<ClientResultException>();

        using (Assert.Multiple())
        {
            await Assert.That(handler.Request.Method).IsEqualTo(HttpMethod.Post);
            // Uri equality includes the query string, so this also proves no api-version is appended.
            await Assert.That(handler.Request.Uri).IsEqualTo(new Uri("https://example.cognitiveservices.azure.com/openai/v1/embeddings"));
            await Assert.That(handler.Request.Authorization).IsEqualTo("Bearer test-token");
            await Assert.That(JsonDocument.Parse(handler.Request.Body).RootElement.GetProperty("model").GetString()).IsEqualTo("embedding-model");
        }
    }

    private static HttpClientPipelineTransport CreateTransport(CapturingHandler handler) => new(new HttpClient(handler));

    private sealed class FakeCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(GetToken(requestContext, cancellationToken));
    }

    /// <summary>Records the first request and answers 404 so the SDK call throws without a real service.</summary>
    private sealed class CapturingHandler : HttpMessageHandler
    {
        private CapturedRequest? _request;

        public CapturedRequest Request => _request
            ?? throw new InvalidOperationException("The client under test never sent a request.");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _request = new CapturedRequest(
                request.Method,
                request.RequestUri ?? throw new InvalidOperationException("The request has no URI."),
                request.Headers.Authorization?.ToString(),
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = JsonContent.Create(new { error = new { message = "Resource not found" } })
            };
        }
    }

    private sealed record CapturedRequest(HttpMethod Method, Uri Uri, string? Authorization, string Body);
}
