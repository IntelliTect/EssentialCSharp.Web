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
    /// Tools not on this list are neither advertised to the model nor executed.
    /// </summary>
    /// <remarks>
    /// When empty and <see cref="AllowAllMcpTools"/> is <c>false</c> (the default), all MCP tool
    /// calls are denied — fail-secure. Set <see cref="AllowAllMcpTools"/> to <c>true</c> to allow
    /// all tools without an explicit list (useful in development environments only).
    /// </remarks>
    public List<string> AllowedMcpTools { get; set; } = [];

    /// <summary>
    /// When <c>true</c>, bypasses the <see cref="AllowedMcpTools"/> allowlist and permits all
    /// MCP tools. Should only be set in non-production environments.
    /// </summary>
    public bool AllowAllMcpTools { get; set; }
}
