using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using EssentialCSharp.Chat;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace EssentialCSharp.Chat.Tests;

/// <summary>
/// Regression tests: every chat request sent to the model provider caps its output and lets the provider truncate a
/// long continued conversation, so one permitted request can't run up an unbounded token bill.
/// </summary>
public class AIChatServiceCostLimitTests
{
    [Test]
    public async Task GetChatCompletion_SendsTheConfiguredOutputCapAndAutoTruncation()
    {
        CapturingHandler handler = new();
        AIChatService service = CreateService(handler, new AIOptions { ChatDeploymentName = "test-deployment", MaxOutputTokens = 1234 });

        await service.GetChatCompletion("hello", previousResponseId: "resp_previous");

        using JsonDocument body = JsonDocument.Parse(handler.LastRequestBody!);
        using (Assert.Multiple())
        {
            await Assert.That(body.RootElement.GetProperty("max_output_tokens").GetInt32()).IsEqualTo(1234);
            await Assert.That(body.RootElement.GetProperty("truncation").GetString()).IsEqualTo("auto");
            await Assert.That(body.RootElement.GetProperty("previous_response_id").GetString()).IsEqualTo("resp_previous");
        }
    }

    [Test]
    public async Task GetChatCompletion_UsesADefaultOutputCapWhenNoneIsConfigured()
    {
        CapturingHandler handler = new();
        AIChatService service = CreateService(handler, new AIOptions { ChatDeploymentName = "test-deployment" });

        await service.GetChatCompletion("hello");

        using JsonDocument body = JsonDocument.Parse(handler.LastRequestBody!);
        await Assert.That(body.RootElement.GetProperty("max_output_tokens").GetInt32()).IsEqualTo(AIOptions.DefaultMaxOutputTokens);
    }

    private static AIChatService CreateService(CapturingHandler handler, AIOptions aiOptions)
    {
#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        ResponsesClientOptions options = new()
        {
            Endpoint = new Uri("http://localhost/openai/deployments/test-deployment"),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
        };
        ResponsesClient responseClient = new(new ApiKeyCredential("test-key"), options);
#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

        return new AIChatService(Options.Create(aiOptions), searchService: null!, responseClient, NullLogger<AIChatService>.Instance);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private const string CompletedResponse =
            """{"id":"resp_new","object":"response","created_at":0,"status":"completed","model":"test-deployment","output":[{"id":"msg_1","type":"message","status":"completed","role":"assistant","content":[{"type":"output_text","text":"hi","annotations":[]}]}]}""";

        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return CreateResponse();
        }

        // The response goes to the HttpClient pipeline, which disposes it.
        private static HttpResponseMessage CreateResponse()
        {
            HttpResponseMessage response = new(HttpStatusCode.OK);
            response.Content = new StringContent(CompletedResponse, Encoding.UTF8, "application/json");
            return response;
        }
    }
}
