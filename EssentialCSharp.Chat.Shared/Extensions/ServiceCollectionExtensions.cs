using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Npgsql;

namespace EssentialCSharp.Chat.Common.Extensions;

public static class ServiceCollectionExtensions
{
    private static readonly string[] _PostgresScopes = ["https://ossrdbms-aad.database.windows.net/.default"];

    /// <summary>
    /// Adds Azure OpenAI and related AI services to the service collection using Managed Identity
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="aiOptions">The AI configuration options</param>
    /// <param name="postgresConnectionString">The PostgreSQL connection string for the vector store</param>
    /// <param name="credential">The token credential to use for authentication. If null, DefaultAzureCredential will be used.</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAzureOpenAIServices(
        this IServiceCollection services,
        AIOptions aiOptions,
        string postgresConnectionString,
        TokenCredential? credential = null)
    {
        // Use DefaultAzureCredential if no credential is provided
        // This works both locally (using Azure CLI, Visual Studio, etc.) and in Azure (using Managed Identity)
        credential ??= new DefaultAzureCredential();

        if (string.IsNullOrEmpty(aiOptions.Endpoint))
        {
            throw new InvalidOperationException("AIOptions.Endpoint is required.");
        }

        var endpoint = new Uri(aiOptions.Endpoint);

        // Register Azure OpenAI services with Managed Identity authentication
#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        services.AddAzureOpenAIChatClient(
            aiOptions.ChatDeploymentName,
            endpoint.ToString(),
            credential);

        services.AddSingleton(provider =>
            new AzureOpenAIClient(endpoint, credential));

        services.AddAzureOpenAIChatCompletion(
            aiOptions.ChatDeploymentName,
            aiOptions.Endpoint,
            credential);

        // Add PostgreSQL vector store with managed identity support
        services.AddPostgresVectorStoreWithManagedIdentity(postgresConnectionString, credential);

        services.AddAzureOpenAIEmbeddingGenerator(
            aiOptions.VectorGenerationDeploymentName,
            aiOptions.Endpoint,
            credential);
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
    /// <param name="credential">Optional token credential to use for authentication. If null, DefaultAzureCredential will be used.</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddAzureOpenAIServices(
        this IServiceCollection services,
        IConfiguration configuration,
        TokenCredential? credential = null)
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

        return services.AddAzureOpenAIServices(aiOptions, postgresConnectionString, credential);
    }

    /// <summary>
    /// Adds PostgreSQL vector store with managed identity authentication support.
    /// Uses periodic token refresh to ensure tokens are renewed before expiry.
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="connectionString">The PostgreSQL connection string (without password)</param>
    /// <param name="credential">The token credential to use for authentication. If null, DefaultAzureCredential will be used.</param>
    /// <returns>The service collection for chaining</returns>
    private static IServiceCollection AddPostgresVectorStoreWithManagedIdentity(
        this IServiceCollection services,
        string connectionString,
        TokenCredential? credential = null)
    {
        credential ??= new DefaultAzureCredential();

        // Register NpgsqlDataSource with UseVector() enabled - this is critical for pgvector support
        services.AddSingleton<NpgsqlDataSource>(sp =>
        {
            var connBuilder = new NpgsqlConnectionStringBuilder(connectionString);
            bool isAzurePostgres = connBuilder.Host?.Contains(".postgres.database.azure.com",
                StringComparison.OrdinalIgnoreCase) ?? false;

            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            // IMPORTANT: UseVector() must be called to enable pgvector support
            dataSourceBuilder.UseVector();

            if (isAzurePostgres && string.IsNullOrEmpty(connBuilder.Password))
            {
                // Ensure SSL is enabled for Azure PostgreSQL
                if (dataSourceBuilder.ConnectionStringBuilder.SslMode < SslMode.Require)
                {
                    dataSourceBuilder.ConnectionStringBuilder.SslMode = SslMode.Require;
                }

                // Use periodic token refresh instead of a one-shot token at startup.
                // Azure AD tokens expire after ~1 hour; refreshing every 50 minutes
                // ensures uninterrupted connectivity for long-running applications.
                dataSourceBuilder.UsePeriodicPasswordProvider(
                    async (_, ct) =>
                    {
                        var tokenRequestContext = new TokenRequestContext(_PostgresScopes);
                        var accessToken = await credential.GetTokenAsync(tokenRequestContext, ct);
                        return accessToken.Token;
                    },
                    TimeSpan.FromMinutes(50),
                    TimeSpan.FromSeconds(10));
            }

            return dataSourceBuilder.Build();
        });

        // Register the vector store using the NpgsqlDataSource from DI
#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        services.AddPostgresVectorStore();
#pragma warning restore SKEXP0010

        return services;
    }

    /// <summary>
    /// Adds Azure OpenAI and related AI services to the service collection using API key authentication (legacy)
    /// </summary>
    /// <param name="services">The service collection to add services to</param>
    /// <param name="aiOptions">The AI configuration options</param>
    /// <param name="postgresConnectionString">The PostgreSQL connection string for the vector store</param>
    /// <param name="apiKey">The API key for Azure OpenAI authentication</param>
    /// <returns>The service collection for chaining</returns>
    [Obsolete("API key authentication is not recommended for production. Use AddAzureOpenAIServices with Managed Identity instead.")]
    public static IServiceCollection AddAzureOpenAIServicesWithApiKey(
        this IServiceCollection services,
        AIOptions aiOptions,
        string postgresConnectionString,
        string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new ArgumentException("API key cannot be null or empty.", nameof(apiKey));
        }

        if (string.IsNullOrEmpty(aiOptions.Endpoint))
        {
            throw new InvalidOperationException("AIOptions.Endpoint is required.");
        }

        var endpoint = new Uri(aiOptions.Endpoint);

        // Register Azure OpenAI services with API key authentication
#pragma warning disable SKEXP0010 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
        services.AddAzureOpenAIChatClient(
            aiOptions.ChatDeploymentName,
            aiOptions.Endpoint,
            apiKey);

        services.AddSingleton(provider =>
            new AzureOpenAIClient(endpoint, new Azure.AzureKeyCredential(apiKey)));

        services.AddAzureOpenAIChatCompletion(
            aiOptions.ChatDeploymentName,
            aiOptions.Endpoint,
            apiKey);

        // Register NpgsqlDataSource with UseVector() enabled for API key scenario as well
        services.AddSingleton<NpgsqlDataSource>(sp =>
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(postgresConnectionString);
            // IMPORTANT: UseVector() must be called to enable pgvector support
            dataSourceBuilder.UseVector();
            return dataSourceBuilder.Build();
        });

        // Add PostgreSQL vector store using the NpgsqlDataSource from DI
        services.AddPostgresVectorStore();

        services.AddAzureOpenAIEmbeddingGenerator(
            aiOptions.VectorGenerationDeploymentName,
            aiOptions.Endpoint,
            apiKey);
#pragma warning restore SKEXP0010

        // Register shared AI services
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<AISearchService>();
        services.AddSingleton<AIChatService>();
        services.AddSingleton<MarkdownChunkingService>();

        return services;
    }
}
