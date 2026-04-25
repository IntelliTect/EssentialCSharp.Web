using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.SemanticKernel;
using Npgsql;

namespace EssentialCSharp.Chat.Common.Extensions;

public static class ServiceCollectionExtensions
{
    private static readonly string[] _PostgresScopes = ["https://ossrdbms-aad.database.windows.net/.default"];

    /// <summary>
    /// Dispatches to <see cref="AddLocalAIServices"/> or <see cref="AddAzureOpenAIServices"/>
    /// based on <c>AIOptions:UseLocalAI</c>. Replaces the <c>if (!IsDevelopment())</c> guard in
    /// Program.cs so that AI services are always registered regardless of environment.
    /// </summary>
    public static IHostApplicationBuilder AddAIServices(
        this IHostApplicationBuilder builder,
        IConfiguration configuration)
    {
        var aiOptions = configuration.GetSection("AIOptions").Get<AIOptions>() ?? new AIOptions();

        if (aiOptions.UseLocalAI)
        {
            builder.AddLocalAIServices(configuration);
        }
        else if (!string.IsNullOrEmpty(aiOptions.Endpoint))
        {
            builder.Services.AddAzureOpenAIServices(configuration);
        }
        else if (!builder.Environment.IsDevelopment())
        {
            // Non-development without an endpoint is a misconfiguration — fail loudly.
            throw new InvalidOperationException(
                "AIOptions:Endpoint is required when UseLocalAI=false in non-development environments. " +
                "Set the endpoint or enable local AI mode with aspire secret set Parameters:UseLocalAI true");
        }
        // else: development + no config — graceful degradation, chat endpoints unavailable.

        return builder;
    }

    /// <summary>
    /// Registers the Ollama-backed local AI services. Uses IChatClient from
    /// CommunityToolkit.Aspire.OllamaSharp. Vector search (RAG) is disabled in Phase 1
    /// due to the embedding dimension mismatch (Ollama nomic-embed-text = 768 dims,
    /// pgvector schema expects 1536).
    /// </summary>
    public static IHostApplicationBuilder AddLocalAIServices(
        this IHostApplicationBuilder builder,
        IConfiguration configuration)
    {
        builder.Services.Configure<AIOptions>(configuration.GetSection("AIOptions"));

        // Registers IChatClient backed by the Ollama "ollama-chat" resource.
        // Connection string injected by Aspire: Endpoint=http://...:11434;Model=qwen2.5-coder:7b
        builder.AddOllamaApiClient("ollama-chat")
               .AddChatClient();

        // NOTE: ollama-embed (nomic-embed-text, 768 dims) not registered in Phase 1.
        // The pgvector schema hardcodes 1536 dims — incompatible without schema migration.
        // Phase 2: register IEmbeddingGenerator + configure VectorStoreCollectionDefinition.

        builder.Services.AddSingleton<IAIChatService, LocalAIChatService>();
        return builder;
    }

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

        // Register shared AI services — forward IAIChatService to the concrete instance
        // so the CLI tool (GetRequiredService<AIChatService>()) and the web app
        // (GetRequiredService<IAIChatService>()) share the same singleton.
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<AISearchService>();
        services.AddSingleton<AIChatService>();
        services.AddSingleton<IAIChatService>(sp => sp.GetRequiredService<AIChatService>());
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

}
