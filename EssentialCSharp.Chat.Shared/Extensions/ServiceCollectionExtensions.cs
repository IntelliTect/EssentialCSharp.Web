using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace EssentialCSharp.Chat.Common.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds Azure OpenAI and related AI services to the service collection
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="aiOptions">The AI configuration options</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAzureOpenAIServices(this IServiceCollection services, AIOptions aiOptions)
    {
        // Validate required configuration
        if (aiOptions == null)
        {
            throw new InvalidOperationException("AIOptions cannot be null.");
        }

        if (string.IsNullOrEmpty(aiOptions.Endpoint) ||
            string.IsNullOrEmpty(aiOptions.ApiKey))
        {
            throw new InvalidOperationException("Azure OpenAI Endpoint and ApiKey must be properly configured in AIOptions. Please update your configuration with valid values.");
        }

        if (string.IsNullOrEmpty(aiOptions.PostgresConnectionString) ||
            aiOptions.PostgresConnectionString.Contains("your-postgres-connection-string"))
        {
            throw new InvalidOperationException("PostgreSQL connection string must be properly configured in AIOptions for vector store. Please update your configuration with a valid connection string.");
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

        // Add PostgreSQL vector store
        services.AddPostgresVectorStore(aiOptions.PostgresConnectionString);

#pragma warning restore SKEXP0010

        // Register shared AI services
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<AISearchService>();
        services.AddSingleton<AIChatService>();
        services.AddSingleton<MarkdownChunkingService>();

        return services;
    }

    /// <summary>
    /// Adds Azure OpenAI and related AI services to the service collection using configuration
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="configuration">The configuration to read AIOptions from</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAzureOpenAIServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure AI options from configuration
        services.Configure<AIOptions>(configuration.GetSection("AIOptions"));

        var aiOptions = configuration.GetSection("AIOptions").Get<AIOptions>();

        return aiOptions == null
            ? throw new InvalidOperationException("AIOptions section is missing from configuration.")
            : services.AddAzureOpenAIServices(aiOptions);
    }
}
