using ModelContextProtocol.Client;
using OpenAI.Responses;

namespace EssentialCSharp.Chat.Common.Services;

public sealed class UnavailableAIChatService : IAIChatService
{
    private static AIChatUnavailableException CreateException() =>
        new(AIConfigurationState.DevelopmentUnavailableMessage);

    public Task<(string response, string responseId)> GetChatCompletion(
        string prompt,
        string? systemPrompt = null,
        string? previousResponseId = null,
        IMcpClient? mcpClient = null,
#pragma warning disable OPENAI001
        IEnumerable<ResponseTool>? tools = null,
        ResponseReasoningEffortLevel? reasoningEffortLevel = null,
#pragma warning restore OPENAI001
        bool enableContextualSearch = false,
        CancellationToken cancellationToken = default) =>
        Task.FromException<(string response, string responseId)>(CreateException());

    public IAsyncEnumerable<(string text, string? responseId)> GetChatCompletionStream(
        string prompt,
        string? systemPrompt = null,
        string? previousResponseId = null,
        IMcpClient? mcpClient = null,
#pragma warning disable OPENAI001
        IEnumerable<ResponseTool>? tools = null,
        ResponseReasoningEffortLevel? reasoningEffortLevel = null,
#pragma warning restore OPENAI001
        bool enableContextualSearch = false,
        CancellationToken cancellationToken = default) =>
        ThrowUnavailable(cancellationToken);

    private static async IAsyncEnumerable<(string text, string? responseId)> ThrowUnavailable(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw CreateException();
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
}
