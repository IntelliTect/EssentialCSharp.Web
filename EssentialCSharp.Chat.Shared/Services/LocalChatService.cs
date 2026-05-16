using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using OpenAI.Responses;

namespace EssentialCSharp.Chat.Common.Services;

public partial class LocalChatService : IChatCompletionService
{
    private const int MaxConversationMessages = 20;
    private readonly AIOptions _Options;
    private readonly IHttpClientFactory _HttpClientFactory;
    private readonly ILogger<LocalChatService> _Logger;
    private readonly ConcurrentDictionary<string, List<LocalChatMessage>> _ConversationHistory = new();

    public bool IsAvailable => true;

    public LocalChatService(IOptions<AIOptions> options, IHttpClientFactory httpClientFactory, ILogger<LocalChatService> logger)
    {
        _Options = options.Value;
        _HttpClientFactory = httpClientFactory;
        _Logger = logger;
    }

    public async Task<(string response, string responseId)> GetChatCompletion(
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
        CancellationToken cancellationToken = default)
    {
        var (client, history, jsonPayload) = PrepareRequest(prompt, systemPrompt, previousResponseId);

        HttpResponseMessage response;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/chat/completions")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "local-dev-key");

        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ChatBackendUnavailableException("Local AI backend is unavailable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChatBackendUnavailableException("Local AI backend timed out.", ex);
        }

        using (response)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                LogLocalRequestFailed(_Logger, (int)response.StatusCode, body);
                throw new ChatBackendUnavailableException("Local AI backend returned a non-success status.");
            }

            try
            {
                var (text, responseId) = ParseResponse(body);
                history.Add(new LocalChatMessage("user", prompt));
                history.Add(new LocalChatMessage("assistant", text));
                _ConversationHistory[responseId] = history.TakeLast(MaxConversationMessages).ToList();
                return (text, responseId);
            }
            catch (Exception ex) when (ex is JsonException || ex is InvalidOperationException || ex is NotSupportedException)
            {
                throw new ChatBackendUnavailableException("Local AI backend returned an invalid response.", ex);
            }
        }
    }

    public async IAsyncEnumerable<(string text, string? responseId)> GetChatCompletionStream(
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
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (response, responseId) = await GetChatCompletion(
            prompt,
            systemPrompt,
            previousResponseId,
            mcpClient,
            tools,
            reasoningEffortLevel,
            enableContextualSearch,
            endUserId,
            cancellationToken);

        if (!string.IsNullOrEmpty(response))
        {
            yield return (response, responseId: null);
        }

        yield return (string.Empty, responseId);
    }

    private (HttpClient Client, List<LocalChatMessage> History, string JsonPayload) PrepareRequest(
        string prompt,
        string? systemPrompt,
        string? previousResponseId = null)
    {
        var client = _HttpClientFactory.CreateClient("LocalAIChat");
        var effectiveSystemPrompt = string.IsNullOrWhiteSpace(systemPrompt) ? _Options.SystemPrompt : systemPrompt;
        var history = ResolveHistory(previousResponseId);

        var messages = new List<object> { new { role = "system", content = effectiveSystemPrompt } };
        messages.AddRange(history.Select(m => new { role = m.Role, content = m.Content }));
        messages.Add(new { role = "user", content = prompt });

        var payload = new
        {
            model = _Options.LocalChatModel,
            messages,
            stream = false
        };

        return (client, history, JsonSerializer.Serialize(payload));
    }

    private List<LocalChatMessage> ResolveHistory(string? previousResponseId)
    {
        if (string.IsNullOrWhiteSpace(previousResponseId))
            return [];

        return _ConversationHistory.TryGetValue(previousResponseId, out var history)
            ? [.. history]
            : [];
    }

    private static (string Text, string ResponseId) ParseResponse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        string responseId = root.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.String
            ? idProp.GetString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");

        if (root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
            && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            if (choice.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var content)
                && content.ValueKind == JsonValueKind.String)
            {
                return (content.GetString() ?? string.Empty, responseId);
            }
        }

        if (root.TryGetProperty("message", out var ollamaMessage)
            && ollamaMessage.TryGetProperty("content", out var ollamaContent)
            && ollamaContent.ValueKind == JsonValueKind.String)
        {
            return (ollamaContent.GetString() ?? string.Empty, responseId);
        }

        throw new InvalidOperationException("Local AI response did not contain any content.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local chat request failed with status {StatusCode}. Body: {Body}")]
    private static partial void LogLocalRequestFailed(ILogger logger, int statusCode, string body);

    private sealed record LocalChatMessage(string Role, string Content);
}
