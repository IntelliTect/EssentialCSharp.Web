namespace EssentialCSharp.Chat;

public class AIOptions
{
    /// <summary>
    /// The Azure OpenAI deployment name for text embedding generation.
    /// </summary>
    public string VectorGenerationDeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// The Azure OpenAI deployment name for chat completions.
    /// </summary>
    public string ChatDeploymentName { get; set; } = string.Empty;

    /// <summary>
    /// The system prompt to use for the chat model.
    /// </summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>
    /// The Azure OpenAI endpoint URL.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// Static allowlist of MCP tool names the AI agent is permitted to invoke.
    /// When non-empty, acts as an outer gate independent of the MCP server channel:
    /// tools not on this list are neither advertised to the model nor executed.
    /// When empty, all tools advertised by the MCP server are allowed (development default).
    /// </summary>
    public List<string> AllowedMcpTools { get; set; } = [];
}
