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
        try
        {
            // Configure AI options from configuration
            services.Configure<AIOptions>(configuration.GetSection("AIOptions"));

            var aiOptions = configuration.GetSection("AIOptions").Get<AIOptions>();
            
            // If AI options are missing or incomplete, log warning and skip registration
            if (aiOptions == null)
            {
                throw new InvalidOperationException("AIOptions section is missing from configuration.");
            }

            // Validate required configuration
            if (string.IsNullOrEmpty(aiOptions.Endpoint) || 
                aiOptions.Endpoint.Contains("your-azure-openai-endpoint") ||
                string.IsNullOrEmpty(aiOptions.ApiKey) || 
                aiOptions.ApiKey.Contains("your-azure-openai-api-key"))
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

            // Register AI services
            services.AddSingleton<EmbeddingService>();
            services.AddSingleton<AISearchService>();
            services.AddSingleton<AIChatService>();
            services.AddSingleton<MarkdownChunkingService>();

            return services;
        }
        catch (Exception)
        {
            // If AI services fail to register, don't register them at all
            // The ChatController will handle the null service gracefully
            throw; // Re-throw so Program.cs can log the specific error
        }
    }
}
