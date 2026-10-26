using Azure.Core;
using OpenAI;
using OpenAI.Embeddings;
using OpenAI.Responses;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace EssentialCSharp.Chat.Common.Services;

/// <summary>
/// Builds plain OpenAI SDK clients for the Azure OpenAI v1 API (<c>{endpoint}/openai/v1</c>).
/// </summary>
/// <remarks>
/// The v1 routes need no <c>api-version</c> and take the deployment name as the request's <c>model</c>
/// (there is no <c>/deployments/{name}</c> route for Responses). Authentication is Entra ID through
/// <see cref="TokenCredential"/>. This follows Microsoft's guidance to use the <c>OpenAI</c> package directly
/// instead of <c>Azure.AI.OpenAI</c>: https://learn.microsoft.com/azure/foundry/openai/api-version-lifecycle
/// </remarks>
public static class AzureOpenAIV1Clients
{
    // The scope the existing resource has been called with so far; keep it until the v1 route is verified with another.
    private const string TokenScope = "https://cognitiveservices.azure.com/.default";

    public static Uri GetV1Endpoint(string endpoint) => new($"{endpoint.TrimEnd('/')}/openai/v1");

#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    public static ResponsesClient CreateResponsesClient(string endpoint, TokenCredential credential, PipelineTransport? transport = null)
    {
        ResponsesClientOptions options = new() { Endpoint = GetV1Endpoint(endpoint) };
        if (transport is not null)
        {
            options.Transport = transport;
        }

        return new ResponsesClient(CreateBearerPolicy(credential), options);
    }

    public static EmbeddingClient CreateEmbeddingClient(string endpoint, string deploymentName, TokenCredential credential, PipelineTransport? transport = null)
    {
        OpenAIClientOptions options = new() { Endpoint = GetV1Endpoint(endpoint) };
        if (transport is not null)
        {
            options.Transport = transport;
        }

        return new EmbeddingClient(deploymentName, CreateBearerPolicy(credential), options);
    }
#pragma warning restore OPENAI001

    private static BearerTokenPolicy CreateBearerPolicy(TokenCredential credential) =>
        new(new CredentialTokenProvider(credential), TokenScope);

    /// <summary>
    /// Bridges Azure.Core's <see cref="TokenCredential"/> into the System.ClientModel
    /// <see cref="AuthenticationTokenProvider"/> abstraction used by <see cref="BearerTokenPolicy"/>.
    /// </summary>
    private sealed class CredentialTokenProvider(TokenCredential credential) : AuthenticationTokenProvider
    {
        public override GetTokenOptions CreateTokenOptions(IReadOnlyDictionary<string, object> context)
            => new(context);

        public override AuthenticationToken GetToken(GetTokenOptions options, CancellationToken cancellationToken)
        {
            AccessToken token = credential.GetToken(new TokenRequestContext([TokenScope]), cancellationToken);
            return new AuthenticationToken(token.Token, "Bearer", token.ExpiresOn, null);
        }

        public override async ValueTask<AuthenticationToken> GetTokenAsync(GetTokenOptions options, CancellationToken cancellationToken)
        {
            AccessToken token = await credential.GetTokenAsync(new TokenRequestContext([TokenScope]), cancellationToken);
            return new AuthenticationToken(token.Token, "Bearer", token.ExpiresOn, null);
        }
    }
}
