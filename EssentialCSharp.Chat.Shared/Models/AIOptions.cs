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
    /// The API key for accessing Azure OpenAI services.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The PostgreSQL connection string for the vector store.
    /// </summary>
    public string PostgresConnectionString { get; set; } = string.Empty;
}
