using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.AI;
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

        services.AddEmbeddingGenerator(sp =>
            sp.GetRequiredService<AzureOpenAIClient>()
              .GetEmbeddingClient(aiOptions.VectorGenerationDeploymentName)
              .AsIEmbeddingGenerator())
            .UseLogging()
            .UseOpenTelemetry();
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
    /// Uses per-connection token refresh via <c>UsePasswordProvider</c>, which calls
    /// <see cref="TokenCredential.GetTokenAsync"/> on every new physical connection.
    /// <see cref="DefaultAzureCredential"/> caches tokens internally and auto-refreshes
    /// ~5 minutes before expiry, so this does not add Azure AD overhead.
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

                var tokenRequestContext = new TokenRequestContext(_PostgresScopes);

                // UsePasswordProvider is called for every new physical connection.
                // DefaultAzureCredential caches tokens internally and auto-refreshes ~5 min before
                // expiry — no extra Azure AD load. This is the approach recommended by the Npgsql
                // docs for cloud providers that implement their own caching (Azure MI does).
                // UsePeriodicPasswordProvider is only for token sources without built-in caching.
                // See: https://www.npgsql.org/doc/security.html
                // See: https://github.com/npgsql/npgsql/issues/5186
                //
                // Note: The username is expected to be set in the connection string already
                // (Aspire sets it during deployment for Azure PostgreSQL Flexible Server).
                // If a standalone username-extraction fallback is ever needed, use the
                // Microsoft.Azure.PostgreSQL.Auth package (UseEntraAuthentication extension)
                // once it ships on NuGet.
                dataSourceBuilder.UsePasswordProvider(
                    passwordProvider: _ => credential.GetToken(tokenRequestContext, default).Token,
                    passwordProviderAsync: async (_, ct) =>
                        (await credential.GetTokenAsync(tokenRequestContext, ct)).Token);

                // Recycle pooled connections after 50 min, well before the 60-min JWT token TTL.
                // Combined with UsePasswordProvider (called on every new physical connection),
                // this ensures no pooled connection ever holds an expired token.
                dataSourceBuilder.ConnectionStringBuilder.ConnectionLifetime = 3000;
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

        services.AddEmbeddingGenerator(sp =>
            sp.GetRequiredService<AzureOpenAIClient>()
              .GetEmbeddingClient(aiOptions.VectorGenerationDeploymentName)
              .AsIEmbeddingGenerator())
            .UseLogging()
            .UseOpenTelemetry();
#pragma warning restore SKEXP0010

        // Register shared AI services
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<AISearchService>();
        services.AddSingleton<AIChatService>();
        services.AddSingleton<MarkdownChunkingService>();

        return services;
    }
}
