using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OpenAI.Responses;

namespace EssentialCSharp.Chat.Common.Services;

/// <summary>
/// Service for handling AI chat completions using the OpenAI Responses API
/// </summary>
public class AIChatService
{
    private readonly AIOptions _Options;
    private readonly AzureOpenAIClient _AzureClient;
    private readonly OpenAIResponseClient _ResponseClient;
    private readonly AISearchService _SearchService;

    public AIChatService(IOptions<AIOptions> options, AISearchService searchService)
    {
        _Options = options.Value;
        _SearchService = searchService;

        // Initialize Azure OpenAI client and get the Response Client from it
        _AzureClient = new AzureOpenAIClient(
            new Uri(_Options.Endpoint),
            new System.ClientModel.ApiKeyCredential(_Options.ApiKey));

        _ResponseClient = _AzureClient.GetOpenAIResponseClient(_Options.ChatDeploymentName);
    }

    /// <summary>
    /// Gets a single chat completion response with all optional features
    /// </summary>
    /// <param name="prompt">The user's input prompt</param>
    /// <param name="systemPrompt">Optional system prompt to override the default</param>
    /// <param name="previousResponseId">Previous response ID to maintain conversation context</param>
    /// <param name="tools">Optional tools for the AI to use</param>
    /// <param name="reasoningEffortLevel">Optional reasoning effort level for reasoning models</param>
    /// <param name="enableContextualSearch">Enable vector search for contextual information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The AI response text and response ID for conversation continuity</returns>
    public async Task<(string response, string responseId)> GetChatCompletion(
        string prompt,
        string? systemPrompt = null,
        string? previousResponseId = null,
        IMcpClient? mcpClient = null,
        IEnumerable<ResponseTool>? tools = null,
        ResponseReasoningEffortLevel? reasoningEffortLevel = null,
        bool enableContextualSearch = false,
        CancellationToken cancellationToken = default)
    {
        var responseOptions = await CreateResponseOptionsAsync(previousResponseId, tools, reasoningEffortLevel, mcpClient: mcpClient, cancellationToken: cancellationToken);
        var enrichedPrompt = await EnrichPromptWithContext(prompt, enableContextualSearch, cancellationToken);
        return await GetChatCompletionCore(enrichedPrompt, responseOptions, systemPrompt, cancellationToken);
    }

    /// <summary>
    /// Gets a streaming chat completion response with all optional features
    /// </summary>
    /// <param name="prompt">The user's input prompt</param>
    /// <param name="systemPrompt">Optional system prompt to override the default</param>
    /// <param name="previousResponseId">Previous response ID to maintain conversation context</param>
    /// <param name="tools">Optional tools for the AI to use</param>
    /// <param name="reasoningEffortLevel">Optional reasoning effort level for reasoning models</param>
    /// <param name="enableContextualSearch">Enable vector search for contextual information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>An async enumerable of response text chunks and final response ID</returns>
    public async IAsyncEnumerable<(string text, string? responseId)> GetChatCompletionStream(
        string prompt,
        string? systemPrompt = null,
        string? previousResponseId = null,
        IMcpClient? mcpClient = null,
        IEnumerable<ResponseTool>? tools = null,
        ResponseReasoningEffortLevel? reasoningEffortLevel = null,
        bool enableContextualSearch = false,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Add logging to help debug the conversation state issue
        Debug.WriteLine($"GetChatCompletionStream called with previousResponseId: {previousResponseId}");

        var responseOptions = await CreateResponseOptionsAsync(previousResponseId, tools, reasoningEffortLevel, mcpClient: mcpClient, cancellationToken: cancellationToken);
        var enrichedPrompt = await EnrichPromptWithContext(prompt, enableContextualSearch, cancellationToken);

        // Construct the user input with system context if provided
        var systemContext = systemPrompt ?? _Options.SystemPrompt;

        // Create the streaming response using the Responses API
        List<ResponseItem> responseItems = [ResponseItem.CreateUserMessageItem(enrichedPrompt)];
        if (systemContext is not null)
        {
            responseItems.Add(
                ResponseItem.CreateSystemMessageItem(systemContext));
        }
        var streamingUpdates = _ResponseClient.CreateResponseStreamingAsync(
            responseItems,
            options: responseOptions,
            cancellationToken: cancellationToken);

        await foreach (var result in ProcessStreamingUpdatesAsync(streamingUpdates, responseOptions, mcpClient, cancellationToken))
        {
            yield return result;
        }
    }

    /// <summary>
    /// Enriches the user prompt with contextual information from vector search
    /// </summary>
    private async Task<string> EnrichPromptWithContext(string prompt, bool enableContextualSearch, CancellationToken cancellationToken)
    {
        if (!enableContextualSearch || _SearchService == null)
        {
            return prompt;
        }

        try
        {
            var searchResults = await _SearchService.ExecuteVectorSearch(prompt);
            var contextualInfo = new System.Text.StringBuilder();

            contextualInfo.AppendLine("## Contextual Information");
            contextualInfo.AppendLine("The following information might be relevant to your question:");
            contextualInfo.AppendLine();

            await foreach (var result in searchResults)
            {
                contextualInfo.AppendLine(System.Globalization.CultureInfo.InvariantCulture, $"**From: {result.Record.Heading}**");
                contextualInfo.AppendLine(result.Record.ChunkText);
                contextualInfo.AppendLine();
            }

            contextualInfo.AppendLine("## User Question");
            contextualInfo.AppendLine(prompt);

            return contextualInfo.ToString();
        }
        catch (Exception ex)
        {
            // Log the error but don't fail the request
            Debug.WriteLine($"Error enriching prompt with context: {ex.Message}");
            return prompt;
        }
    }

    /// <summary>
    /// Processes streaming updates from the OpenAI Responses API, handling both regular responses and function calls
    /// </summary>
    private async IAsyncEnumerable<(string text, string? responseId)> ProcessStreamingUpdatesAsync(
        IAsyncEnumerable<StreamingResponseUpdate> streamingUpdates,
        ResponseCreationOptions responseOptions,
        IMcpClient? mcpClient,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in streamingUpdates.WithCancellation(cancellationToken))
        {
            string? responseId;
            if (update is StreamingResponseCreatedUpdate created)
            {
                // Remember the response ID for later function calls
                responseId = created.Response.Id;
            }
            else if (update is StreamingResponseOutputItemDoneUpdate itemDone)
            {
                // Check if this is a function call that needs to be executed
                if (itemDone.Item is FunctionCallResponseItem functionCallItem && mcpClient != null)
                {
                    // Execute the function call and stream its response
                    await foreach (var functionResult in ExecuteFunctionCallAsync(functionCallItem, responseOptions, mcpClient, cancellationToken))
                    {
                        if (functionResult.responseId != null)
                        {
                            responseId = functionResult.responseId;
                        }
                        yield return functionResult;
                    }
                }
            }
            else if (update is StreamingResponseOutputTextDeltaUpdate deltaUpdate)
            {
                yield return (deltaUpdate.Delta.ToString(), null);
            }
            else if (update is StreamingResponseCompletedUpdate completedUpdate)
            {
                yield return (string.Empty, responseId: completedUpdate.Response.Id); // Signal completion with response ID
            }
        }
    }

    /// <summary>
    /// Executes a function call and streams the response
    /// </summary>
    private async IAsyncEnumerable<(string text, string? responseId)> ExecuteFunctionCallAsync(
        FunctionCallResponseItem functionCallItem,
        ResponseCreationOptions responseOptions,
        IMcpClient mcpClient,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // A dictionary of arguments to pass to the tool. Each key represents a parameter name, and its associated value represents the argument value.
        Dictionary<string, object?> arguments = [];
        // example JsonResponse:
        // "{\"question\":\"Azure OpenAI Responses API (Preview)\"}"
        var jsonResponse = functionCallItem.FunctionArguments.ToString();
        var jsonArguments = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(jsonResponse) ?? new Dictionary<string, object?>();

        // Convert JsonElement values to their actual types
        foreach (var kvp in jsonArguments)
        {
            if (kvp.Value is System.Text.Json.JsonElement jsonElement)
            {
                arguments[kvp.Key] = jsonElement.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.String => jsonElement.GetString(),
                    System.Text.Json.JsonValueKind.Number => jsonElement.GetDecimal(),
                    System.Text.Json.JsonValueKind.True => true,
                    System.Text.Json.JsonValueKind.False => false,
                    System.Text.Json.JsonValueKind.Null => null,
                    _ => jsonElement.ToString()
                };
            }
            else
            {
                arguments[kvp.Key] = kvp.Value;
            }
        }

        // Execute the function call using the MCP client
        var toolResult = await mcpClient.CallToolAsync(
            functionCallItem.FunctionName,
            arguments: arguments,
            cancellationToken: cancellationToken);

        // Create input items with both the function call and the result
        // This matches the Python pattern: append both tool_call and result
        var inputItems = new List<ResponseItem>
        {
            functionCallItem, // The original function call
            new FunctionCallOutputResponseItem(functionCallItem.CallId, string.Join("", toolResult.Content.Where(x => x.Type == "text").OfType<TextContentBlock>().Select(x => x.Text)))
        };

        // Stream the function call response using the same processing logic
        var functionResponseStream = _ResponseClient.CreateResponseStreamingAsync(
            inputItems,
            responseOptions,
            cancellationToken);

        await foreach (var result in ProcessStreamingUpdatesAsync(functionResponseStream, responseOptions, mcpClient, cancellationToken))
        {
            yield return result;
        }
    }

    /// <summary>
    /// Creates response options with optional features
    /// </summary>
    private static async Task<ResponseCreationOptions> CreateResponseOptionsAsync(
        string? previousResponseId = null,
        IEnumerable<ResponseTool>? tools = null,
        ResponseReasoningEffortLevel? reasoningEffortLevel = null,
        IMcpClient? mcpClient = null,
        CancellationToken cancellationToken = default
        )
    {
        var options = new ResponseCreationOptions();

        // Add conversation context if available
        if (!string.IsNullOrEmpty(previousResponseId))
        {
            options.PreviousResponseId = previousResponseId;
        }

        // Add tools if provided
        if (tools != null)
        {
            foreach (var tool in tools)
            {
                options.Tools.Add(tool);
            }
        }

        if (mcpClient is not null)
        {
            await foreach (McpClientTool tool in mcpClient.EnumerateToolsAsync(cancellationToken: cancellationToken))
            {
                options.Tools.Add(ResponseTool.CreateFunctionTool(tool.Name, tool.Description, BinaryData.FromString(tool.JsonSchema.GetRawText())));
            }
        }

        // Add reasoning options if specified
        if (reasoningEffortLevel.HasValue)
        {
            options.ReasoningOptions = new ResponseReasoningOptions()
            {
                ReasoningEffortLevel = reasoningEffortLevel.Value
            };
        }

        return options;
    }

    /// <summary>
    /// Core method for getting chat completions with configurable response options
    /// </summary>
    private async Task<(string response, string responseId)> GetChatCompletionCore(
        string prompt,
        ResponseCreationOptions responseOptions,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        // Construct the user input with system context if provided
        var systemContext = systemPrompt ?? _Options.SystemPrompt;

        // Create the streaming response using the Responses API
        List<ResponseItem> responseItems = [ResponseItem.CreateUserMessageItem(prompt)];
        if (systemContext is not null)
        {
            responseItems.Add(
                ResponseItem.CreateSystemMessageItem(systemContext));
        }

        // Create the response using the Responses API
        var response = await _ResponseClient.CreateResponseAsync(
            responseItems,
            options: responseOptions,
            cancellationToken: cancellationToken);

        // Extract the message content and response ID
        string responseText = string.Empty;
        string responseId = response.Value.Id;

        foreach (var outputItem in response.Value.OutputItems)
        {
            if (outputItem is MessageResponseItem messageItem &&
                messageItem.Role == MessageRole.Assistant)
            {
                var textContent = messageItem.Content?.FirstOrDefault()?.Text;
                if (!string.IsNullOrEmpty(textContent))
                {
                    responseText = textContent;
                    break;
                }
            }
        }

        return (responseText, responseId);
    }

    // TODO: Look into using UserSecurityContext (https://learn.microsoft.com/en-us/azure/defender-for-cloud/gain-end-user-context-ai)
}
