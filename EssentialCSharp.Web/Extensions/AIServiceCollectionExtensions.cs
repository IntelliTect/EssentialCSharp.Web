using EssentialCSharp.Chat.Common.Services;
using EssentialCSharp.Chat;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel;

namespace EssentialCSharp.Web.Extensions;

public static class AIServiceCollectionExtensions
{
    /// <summary>
    /// Adds AI chat services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddAIChatServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure AI options from configuration
        services.Configure<AIOptions>(configuration.GetSection("AIOptions"));

        var aiOptions = configuration.GetSection("AIOptions").Get<AIOptions>()
            ?? throw new InvalidOperationException("AIOptions section is missing or not configured correctly in appsettings.json or environment variables.");

        // Validate required configuration
        if (string.IsNullOrEmpty(aiOptions.Endpoint) || string.IsNullOrEmpty(aiOptions.ApiKey))
        {
            throw new InvalidOperationException("Azure OpenAI Endpoint and ApiKey must be configured in AIOptions.");
        }

        if (string.IsNullOrEmpty(aiOptions.PostgresConnectionString))
        {
            throw new InvalidOperationException("PostgreSQL connection string must be configured in AIOptions for vector store.");
        }


#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates.
        // Register Azure OpenAI services
        services.AddAzureOpenAIEmbeddingGenerator(
        aiOptions.VectorGenerationDeploymentName,
        aiOptions.Endpoint,
        aiOptions.ApiKey);

        services.AddAzureOpenAIChatClient(
                    aiOptions.ChatDeploymentName,
                    aiOptions.Endpoint,
                    aiOptions.ApiKey);

#pragma warning restore SKEXP0010

        // Add PostgreSQL vector store
        services.AddPostgresVectorStore(
        aiOptions.PostgresConnectionString
        );

        // Register AI services
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<AISearchService>();
        services.AddSingleton<AIChatService>();
        services.AddSingleton<MarkdownChunkingService>();

        return services;
    }
}
