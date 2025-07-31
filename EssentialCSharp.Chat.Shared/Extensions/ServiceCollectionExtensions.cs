using Azure.AI.OpenAI;
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
    /// <param name="postgresConnectionString">The PostgreSQL connection string for the vector store</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAzureOpenAIServices(this IServiceCollection services, AIOptions aiOptions, string postgresConnectionString)
    {
        if (string.IsNullOrEmpty(aiOptions.Endpoint) ||
            string.IsNullOrEmpty(aiOptions.ApiKey))
            // Register Azure OpenAI services
#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
            services.AddAzureOpenAIEmbeddingGenerator(
                aiOptions.VectorGenerationDeploymentName,
                aiOptions.Endpoint,
                aiOptions.ApiKey);

        services.AddAzureOpenAIChatClient(
            aiOptions.ChatDeploymentName,
            aiOptions.Endpoint,
            aiOptions.ApiKey);

        services.AddSingleton(provider =>
            new AzureOpenAIClient(new Uri(aiOptions.Endpoint), new Azure.AzureKeyCredential(aiOptions.ApiKey)));

        // Register Azure OpenAI services
        services.AddAzureOpenAIEmbeddingGenerator(
            aiOptions.VectorGenerationDeploymentName,
            aiOptions.Endpoint,
            aiOptions.ApiKey);

        services.AddAzureOpenAIChatCompletion(
            aiOptions.ChatDeploymentName,
            aiOptions.Endpoint,
            aiOptions.ApiKey);

        // Add PostgreSQL vector store
        services.AddPostgresVectorStore(postgresConnectionString);

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
        if (aiOptions == null)
        {
            throw new InvalidOperationException("AIOptions section is missing from configuration.");
        }

        // Get PostgreSQL connection string using the standard method
        var postgresConnectionString = configuration.GetConnectionString("PostgresVectorStore") ??
            throw new InvalidOperationException("Connection string 'PostgresVectorStore' not found.");

        return services.AddAzureOpenAIServices(aiOptions, postgresConnectionString);
    }
}
