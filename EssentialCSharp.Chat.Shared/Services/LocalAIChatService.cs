using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using OpenAI.Responses;

namespace EssentialCSharp.Chat.Common.Services;

/// <summary>
/// Local AI chat service using IChatClient (e.g. Ollama via CommunityToolkit.Aspire.OllamaSharp).
/// Compared to the Azure path: conversation history is in-memory only (lost on restart),
/// ResponseTool/ReasoningEffortLevel params are silently ignored, and vector search (RAG)
/// is disabled. Intended for local development without Azure credentials.
/// </summary>
public class LocalAIChatService : IAIChatService
{
    private readonly IChatClient _chatClient;
    private readonly AIOptions _options;
    private readonly ILogger<LocalAIChatService> _logger;

    // Synthetic conversation history keyed by GUID responseId.
    // In-memory only — not shared across instances and lost on restart.
    // ConcurrentDictionary prevents crashes from parallel requests (e.g., two chat tabs).
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _conversations = new();

    public LocalAIChatService(
        IOptions<AIOptions> options,
        IChatClient chatClient,
        ILogger<LocalAIChatService> logger)
    {
        _options = options.Value;
        _chatClient = chatClient;
        _logger = logger;
    }

    public async Task<(string response, string responseId)> GetChatCompletion(
        string prompt,
        string? systemPrompt = null,
        string? previousResponseId = null,
        IMcpClient? mcpClient = null,
#pragma warning disable OPENAI001
        IEnumerable<ResponseTool>? tools = null,
        ResponseReasoningEffortLevel? reasoningEffortLevel = null,
#pragma warning restore OPENAI001
        bool enableContextualSearch = false,
        CancellationToken cancellationToken = default)
    {
        WarnUnsupportedFeatures(tools, reasoningEffortLevel, enableContextualSearch);

        var messages = BuildMessages(prompt, systemPrompt, previousResponseId);
        var response = await _chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);
        var responseText = response.Text ?? string.Empty;
        var responseId = SaveConversation(messages, responseText, previousResponseId);
        return (responseText, responseId);
    }

    public async IAsyncEnumerable<(string text, string? responseId)> GetChatCompletionStream(
        string prompt,
        string? systemPrompt = null,
        string? previousResponseId = null,
        IMcpClient? mcpClient = null,
#pragma warning disable OPENAI001
        IEnumerable<ResponseTool>? tools = null,
        ResponseReasoningEffortLevel? reasoningEffortLevel = null,
#pragma warning restore OPENAI001
        bool enableContextualSearch = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        WarnUnsupportedFeatures(tools, reasoningEffortLevel, enableContextualSearch);

        var messages = BuildMessages(prompt, systemPrompt, previousResponseId);
        var fullResponse = new System.Text.StringBuilder();

        await foreach (var update in _chatClient.GetStreamingResponseAsync(messages, cancellationToken: cancellationToken))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                fullResponse.Append(update.Text);
                yield return (update.Text, null);
            }
        }

        var responseId = SaveConversation(messages, fullResponse.ToString(), previousResponseId);
        yield return (string.Empty, responseId);
    }

#pragma warning disable OPENAI001
    private void WarnUnsupportedFeatures(
        IEnumerable<ResponseTool>? tools,
        ResponseReasoningEffortLevel? reasoningEffortLevel,
        bool enableContextualSearch)
#pragma warning restore OPENAI001
    {
        if (tools is not null || reasoningEffortLevel is not null)
        {
            _logger.LogWarning("LocalAIChatService: ResponseTool and ReasoningEffortLevel are Azure-specific and are ignored in local mode.");
        }

        if (enableContextualSearch)
        {
            _logger.LogWarning("LocalAIChatService: Vector search (RAG) is disabled in local mode (Phase 1). Run in Azure mode to enable contextual search.");
        }
    }

    private List<ChatMessage> BuildMessages(string prompt, string? systemPrompt, string? previousResponseId)
    {
        var messages = new List<ChatMessage>();

        var sys = string.IsNullOrWhiteSpace(systemPrompt) ? _options.SystemPrompt : systemPrompt;
        if (!string.IsNullOrWhiteSpace(sys))
            messages.Add(new ChatMessage(ChatRole.System, sys));

        if (previousResponseId is not null && _conversations.TryGetValue(previousResponseId, out var history))
            messages.AddRange(history);

        messages.Add(new ChatMessage(ChatRole.User, prompt));
        return messages;
    }

    private string SaveConversation(List<ChatMessage> messages, string assistantResponse, string? previousResponseId)
    {
        var history = messages.Where(m => m.Role != ChatRole.System).ToList();
        history.Add(new ChatMessage(ChatRole.Assistant, assistantResponse));

        var newId = Guid.NewGuid().ToString("N");
        _conversations[newId] = history;

        // Remove previous entry to avoid unbounded memory growth.
        // TryRemove is safe on ConcurrentDictionary.
        if (previousResponseId is not null)
            _conversations.TryRemove(previousResponseId, out _);

        return newId;
    }
}
