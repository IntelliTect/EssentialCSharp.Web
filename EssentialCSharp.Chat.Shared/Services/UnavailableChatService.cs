using ModelContextProtocol.Client;
using OpenAI.Responses;

namespace EssentialCSharp.Chat.Common.Services;

public class UnavailableChatService : IChatCompletionService
{
    public bool IsAvailable => false;

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
        return Task.FromResult<(string response, string responseId)>(
            ("Chat service is unavailable in this environment.", Guid.NewGuid().ToString("N")));
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
        yield return ("Chat service is unavailable in this environment.", responseId: null);
        yield return (string.Empty, Guid.NewGuid().ToString("N"));
        await Task.CompletedTask;
    }
}
