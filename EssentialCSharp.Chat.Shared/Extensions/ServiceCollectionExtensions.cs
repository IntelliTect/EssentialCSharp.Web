using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.SemanticKernel;
using Npgsql;
using Polly;

namespace EssentialCSharp.Chat.Common.Extensions;

public static class ServiceCollectionExtensions
{
    private static readonly string[] PostgresScopes = ["https://ossrdbms-aad.database.windows.net/.default"];

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

        // Configure HTTP resilience for Azure OpenAI requests
        ConfigureAzureOpenAIResilience(services);

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
    /// Configures HTTP resilience (retry, circuit breaker, timeout) for Azure OpenAI HTTP clients.
    /// This handles rate limiting (HTTP 429) and transient errors with exponential backoff.
    /// </summary>
    /// <param name="services">The service collection to configure</param>
    private static void ConfigureAzureOpenAIResilience(IServiceCollection services)
    {
        // Configure resilience for all HTTP clients used by Azure OpenAI services
        services.ConfigureHttpClientDefaults(httpClientBuilder =>
        {
            httpClientBuilder.AddStandardResilienceHandler(options =>
            {
                // Configure retry strategy for rate limiting and transient errors
                options.Retry.MaxRetryAttempts = 5;
                options.Retry.Delay = TimeSpan.FromSeconds(2);
                options.Retry.BackoffType = DelayBackoffType.Exponential;
                options.Retry.UseJitter = true;
                
                // The standard resilience handler already handles:
                // - HTTP 429 (Too Many Requests / Rate Limit)
                // - HTTP 408 (Request Timeout)
                // - HTTP 5xx (Server Errors)
                // - Respects Retry-After header automatically
                
                // Configure circuit breaker to prevent overwhelming the service
                options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
                options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
                options.CircuitBreaker.FailureRatio = 0.2; // Break if 20% of requests fail
                
                // Configure timeout for individual attempts
                options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(30);
                
                // Configure total timeout for all retry attempts
                options.TotalRequestTimeout.Timeout = TimeSpan.FromMinutes(3);
            });
        });
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
    /// NOTE: Token is obtained once at startup and will expire after ~1 hour. 
    /// For long-running applications, consider implementing token refresh logic.
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

        // Parse the connection string to extract host, database, and username
        var builder = new NpgsqlConnectionStringBuilder(connectionString);

        // Check if this is an Azure PostgreSQL connection (contains .postgres.database.azure.com)
        bool isAzurePostgres = builder.Host?.Contains(".postgres.database.azure.com", StringComparison.OrdinalIgnoreCase) ?? false;

        if (isAzurePostgres && string.IsNullOrEmpty(builder.Password))
        {
            // Get access token for Azure PostgreSQL using managed identity
            var tokenRequestContext = new TokenRequestContext(PostgresScopes);
            var accessToken = credential.GetToken(tokenRequestContext, default);

            // Set the password to the access token
            builder.Password = accessToken.Token;

            // Ensure SSL is enabled for Azure
            if (builder.SslMode == SslMode.Disable)
            {
                builder.SslMode = SslMode.Require;
            }

            connectionString = builder.ToString();
        }

        // Register NpgsqlDataSource with UseVector() enabled - this is critical for pgvector support
        services.AddSingleton<NpgsqlDataSource>(sp =>
        {
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
            // IMPORTANT: UseVector() must be called to enable pgvector support
            dataSourceBuilder.UseVector();
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

        // Configure HTTP resilience for Azure OpenAI requests
        ConfigureAzureOpenAIResilience(services);

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
