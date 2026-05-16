using ModelContextProtocol.Client;
using OpenAI.Responses;

namespace EssentialCSharp.Chat.Common.Services;

public interface IChatCompletionService
{
    bool IsAvailable { get; }

    Task<(string response, string responseId)> GetChatCompletion(
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
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<(string text, string? responseId)> GetChatCompletionStream(
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
        CancellationToken cancellationToken = default);
}
