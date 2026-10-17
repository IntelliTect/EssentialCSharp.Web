using System.Net;
using System.Net.Sockets;
using System.Text;
using System.ClientModel;
using EssentialCSharp.Chat;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace EssentialCSharp.Chat.Tests;

public class AIChatServiceModerationTests
{
    [Test]
    public async Task GetChatCompletion_WhenAzure400BodyIdentifiesContentFilter_ThrowsFilteredException()
    {
        await AssertProviderErrorIsFiltered(
            HttpStatusCode.BadRequest,
            """{"error":{"code":"content_filter","message":"policy details"}}""");
    }

    [Test]
    public async Task GetChatCompletion_WhenAzure403NestedBodyIdentifiesPolicyViolation_ThrowsFilteredException()
    {
        await AssertProviderErrorIsFiltered(
            HttpStatusCode.Forbidden,
            """{"error":{"innererror":{"code":"ResponsibleAIPolicyViolation"}}}""");
    }

    [Test]
    public async Task GetChatCompletion_WhenAzureErrorDoesNotIdentifyContentFilter_PreservesProviderException()
    {
        await using var provider = await StubResponsesApi.StartAsync(
            HttpStatusCode.BadRequest,
            """{"error":{"code":"invalid_request","message":"The request is invalid."}}""");
        var service = CreateService(provider.Endpoint);

        await Assert.ThrowsAsync<ClientResultException>(() => service.GetChatCompletion("hello"));
    }

    [Test]
    public async Task GetChatCompletion_WhenNonPolicyServerErrorMentionsContentFilter_PreservesProviderException()
    {
        await using var provider = await StubResponsesApi.StartAsync(
            HttpStatusCode.InternalServerError,
            """{"error":{"code":"content_filter","message":"internal failure"}}""");
        var service = CreateService(provider.Endpoint);

        await Assert.ThrowsAsync<ClientResultException>(() => service.GetChatCompletion("hello"));
    }

    [Test]
    public async Task GetChatCompletion_WhenSuccessfulResponseIsIncompleteForContentFilter_ThrowsFilteredException()
    {
        await using var provider = await StubResponsesApi.StartAsync(
            HttpStatusCode.OK,
            IncompleteResponse("content_filter", includePartialOutput: true));
        var service = CreateService(provider.Endpoint);

        await Assert.ThrowsAsync<ChatContentFilteredException>(() => service.GetChatCompletion("hello"));
    }

    [Test]
    public async Task GetChatCompletion_WhenSuccessfulResponseIsIncompleteForOtherReason_PreservesResponse()
    {
        const string responseId = "resp_incomplete_ordinary";
        await using var provider = await StubResponsesApi.StartAsync(
            HttpStatusCode.OK,
            IncompleteResponse("max_output_tokens", responseId));
        var service = CreateService(provider.Endpoint);

        var (response, actualResponseId) = await service.GetChatCompletion("hello");

        await Assert.That(response).IsEqualTo(string.Empty);
        await Assert.That(actualResponseId).IsEqualTo(responseId);
    }

    private static async Task AssertProviderErrorIsFiltered(HttpStatusCode statusCode, string body)
    {
        await using var provider = await StubResponsesApi.StartAsync(statusCode, body);
        var service = CreateService(provider.Endpoint);

        await Assert.ThrowsAsync<ChatContentFilteredException>(() => service.GetChatCompletion("hello"));
    }

    private static AIChatService CreateService(string endpoint)
    {
#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        var options = new ResponsesClientOptions
        {
            Endpoint = new Uri($"{endpoint}/openai/deployments/test-deployment")
        };
        var responseClient = new ResponsesClient(new ApiKeyCredential("test-key"), options);
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        return new AIChatService(
            Options.Create(new AIOptions { ChatDeploymentName = "test-deployment" }),
            searchService: null!,
            responseClient,
            NullLogger<AIChatService>.Instance);
    }

    private static string IncompleteResponse(
        string reason,
        string responseId = "resp_incomplete",
        bool includePartialOutput = false)
    {
        string output = includePartialOutput
            ? """[{"id":"msg_partial","type":"message","status":"incomplete","role":"assistant","content":[{"type":"output_text","text":"partial output","annotations":[]}]}]"""
            : "[]";
        return $$"""{"id":"{{responseId}}","object":"response","created_at":0,"status":"incomplete","incomplete_details":{"reason":"{{reason}}"},"model":"test-deployment","output":{{output}}}""";
    }

    private sealed class StubResponsesApi : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;
        private readonly Task _responseTask;

        public string Endpoint { get; }

        private StubResponsesApi(HttpListener listener, int port, HttpStatusCode statusCode, string body)
        {
            _listener = listener;
            _statusCode = statusCode;
            _body = body;
            Endpoint = $"http://localhost:{port}";
            _responseTask = RespondAsync();
        }

        public static Task<StubResponsesApi> StartAsync(HttpStatusCode statusCode, string body)
        {
            var portListener = new TcpListener(IPAddress.Loopback, 0);
            portListener.Start();
            int port = ((IPEndPoint)portListener.LocalEndpoint).Port;
            portListener.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://localhost:{port}/");
            listener.Start();
            var provider = new StubResponsesApi(listener, port, statusCode, body);
            return Task.FromResult(provider);
        }

        private async Task RespondAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException) when (!_listener.IsListening)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                context.Response.StatusCode = (int)_statusCode;
                context.Response.ContentType = "application/json";
                byte[] responseBody = Encoding.UTF8.GetBytes(_body);
                context.Response.ContentLength64 = responseBody.Length;
                await context.Response.OutputStream.WriteAsync(responseBody);
                context.Response.Close();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _listener.Close();
            try
            {
                await _responseTask;
            }
            catch (HttpListenerException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
