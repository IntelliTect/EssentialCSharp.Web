using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
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
        _ = await Assert.ThrowsAsync<ClientResultException>(async () => await client.CreateResponseAsync(options));

        await Assert.That(handler.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Uri!.GetLeftPart(UriPartial.Path)).IsEqualTo("https://example.cognitiveservices.azure.com/openai/v1/responses");
        await Assert.That(handler.Uri.Query).IsEmpty();
        await Assert.That(handler.Authorization).IsEqualTo("Bearer test-token");
        await Assert.That(JsonDocument.Parse(handler.Body!).RootElement.GetProperty("model").GetString()).IsEqualTo("web-chat");
#pragma warning restore OPENAI001
    }

    [Test]
    public async Task EmbeddingClient_PostsToV1EmbeddingsWithBearerTokenAndDeploymentAsModel()
    {
        CapturingHandler handler = new();
        var client = AzureOpenAIV1Clients.CreateEmbeddingClient(Endpoint, "embedding-model", new FakeCredential(), CreateTransport(handler));

        _ = await Assert.ThrowsAsync<ClientResultException>(async () => await client.GenerateEmbeddingAsync("hello"));

        await Assert.That(handler.Method).IsEqualTo(HttpMethod.Post);
        await Assert.That(handler.Uri!.GetLeftPart(UriPartial.Path)).IsEqualTo("https://example.cognitiveservices.azure.com/openai/v1/embeddings");
        await Assert.That(handler.Uri.Query).IsEmpty();
        await Assert.That(handler.Authorization).IsEqualTo("Bearer test-token");
        await Assert.That(JsonDocument.Parse(handler.Body!).RootElement.GetProperty("model").GetString()).IsEqualTo("embedding-model");
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
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? Authorization { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            Authorization = request.Headers.Authorization?.ToString();
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Resource not found\"}}", Encoding.UTF8, "application/json")
            };
        }
    }
}
