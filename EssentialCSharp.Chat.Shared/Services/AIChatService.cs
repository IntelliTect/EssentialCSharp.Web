using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace EssentialCSharp.Chat.Common.Services;

public class AIChatService(AzureOpenAIClient aIClient, IOptions<AIOptions> options)
{
    private readonly AIOptions _Options = options.Value;
    private ChatClient ChatClient { get; } = aIClient.GetChatClient(options.Value.ChatDeploymentName);

    public async Task<string> GetChatCompletion(string prompt)
    {
        var response = await ChatClient.CompleteChatAsync([
            new SystemChatMessage(_Options.SystemPrompt),
            new UserChatMessage(prompt)
        ]);

        // Todo: Handle response errors and check for multiple messages?
        return response.Value.Content[0].Text;
    }

    // TODO: Implement streaming chat completions
    public AsyncCollectionResult<StreamingChatCompletionUpdate> GetChatCompletionStream(string prompt)
    {
        return ChatClient.CompleteChatStreamingAsync([
            new SystemChatMessage(_Options.SystemPrompt),
            new UserChatMessage(prompt)
        ]);
    }

    // TODO: Implement batch chat completions for hybrid search
    //    public async Task<IAsyncEnumerable<string>> GetBatchChatCompletionsAsync(IEnumerable<string> prompts)
    //    {
    //#pragma warning disable OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
    //        var batchClient = aIClient.GetBatchClient();
    //#pragma warning restore OPENAI001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.

    //    }

    // TODO: Look into using UserSecurityContext (https://learn.microsoft.com/en-us/azure/defender-for-cloud/gain-end-user-context-ai)
}
