using ModelContextProtocol.Client;
using OpenAI.Responses;

namespace EssentialCSharp.Chat.Common.Services;

public class UnavailableChatService : IChatCompletionService
{
    public bool IsAvailable => false;
    public bool SupportsContextualSearch => false;

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
        CancellationToken cancellationToken = default)
    {
        throw new ChatBackendUnavailableException("Chat service is unavailable in this environment.");
    }

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
        CancellationToken cancellationToken = default)
    {
        throw new ChatBackendUnavailableException("Chat service is unavailable in this environment.");
    }
}
