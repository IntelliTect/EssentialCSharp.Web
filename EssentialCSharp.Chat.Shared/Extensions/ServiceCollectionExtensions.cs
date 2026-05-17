using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using EssentialCSharp.Chat.Common.Models;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Npgsql;

namespace EssentialCSharp.Chat.Common.Extensions;

public static class ServiceCollectionExtensions
{
    private static readonly string[] _PostgresScopes = ["https://ossrdbms-aad.database.windows.net/.default"];
    private const string LocalChatHttpClientName = "LocalAIChat";

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

        // Ensure options are available even when caller provides AIOptions directly.
        services.AddOptions<EmbeddingRetryOptions>()
            .ValidateDataAnnotations()
            .Validate(options =>
            {
                options.Validate();
                return true;
            }, "Embedding retry configuration is invalid.");

        // Register shared AI services
        services.AddSingleton<EmbeddingService>();
        services.AddSingleton<AISearchService>();
        services.AddSingleton<AIChatService>();
        services.AddSingleton<MarkdownChunkingService>();

        return services;
    }

    /// <summary>
    /// Registers chat services using configuration-driven backend selection.
    /// This method never throws for missing or partial AI configuration; it falls back to
    /// <see cref="UnavailableChatService"/> so the app can continue running.
    /// </summary>
    public static IServiceCollection AddConfiguredChatServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AIOptions>(configuration.GetSection("AIOptions"));

        var aiOptions = configuration.GetSection("AIOptions").Get<AIOptions>() ?? new AIOptions();
        string? postgresConnectionString = configuration.GetConnectionString("PostgresVectorStore");

        bool hasAzureEndpoint = !string.IsNullOrWhiteSpace(aiOptions.Endpoint);
        bool hasAzureChatDeployment = !string.IsNullOrWhiteSpace(aiOptions.ChatDeploymentName);
        bool hasAzureVectorDeployment = !string.IsNullOrWhiteSpace(aiOptions.VectorGenerationDeploymentName);
        bool hasAzureConfig = hasAzureEndpoint && hasAzureChatDeployment && hasAzureVectorDeployment
            && IsValidNpgsqlConnectionString(postgresConnectionString);

        string localEndpoint = ResolveLocalEndpoint(aiOptions, configuration);
        bool hasLocalConfig = aiOptions.UseLocalAI
            && !string.IsNullOrWhiteSpace(localEndpoint)
            && !string.IsNullOrWhiteSpace(aiOptions.LocalChatModel);

        if (hasAzureConfig)
        {
            // Pre-validate endpoint URI to avoid exceptions in AddAzureOpenAIServices for
            // non-empty but invalid endpoint values.
            if (!Uri.TryCreate(aiOptions.Endpoint, UriKind.Absolute, out var azureUri)
                || (azureUri.Scheme != Uri.UriSchemeHttp && azureUri.Scheme != Uri.UriSchemeHttps))
            {
                Console.Error.WriteLine("[AI] Azure endpoint is not a valid http/https URI. Falling back to local/unavailable.");
            }
            else
            {
                services.AddAzureOpenAIServices(aiOptions, postgresConnectionString!);
                // Bind EmbeddingRetry from config so operator appsettings/env overrides are honored.
                // The AIOptions overload of AddAzureOpenAIServices only registers validation, not config binding.
                services.AddOptions<EmbeddingRetryOptions>()
                    .Bind(configuration.GetSection(EmbeddingRetryOptions.SectionPath));
                services.AddSingleton<IChatCompletionService>(provider => provider.GetRequiredService<AIChatService>());
                Console.WriteLine("[AI] Selected backend: Azure/Foundry.");
                return services;
            }
        }

        if (hasLocalConfig)
        {
            if (!Uri.TryCreate(localEndpoint, UriKind.Absolute, out var localEndpointUri)
                || (localEndpointUri.Scheme != Uri.UriSchemeHttp && localEndpointUri.Scheme != Uri.UriSchemeHttps))
            {
                services.AddSingleton<IChatCompletionService, UnavailableChatService>();
                Console.Error.WriteLine("[AI] Local backend selected but LocalEndpoint is invalid. Falling back to unavailable backend.");
                return services;
            }

#pragma warning disable EXTEXP0001
            services.AddHttpClient(LocalChatHttpClientName, client =>
            {
                client.BaseAddress = localEndpointUri;
                client.Timeout = TimeSpan.FromSeconds(120);
            })
            // Disable the global standard resilience handler (set by ConfigureHttpClientDefaults
            // in Program.cs). Its default attempt timeout (30s) and total timeout (90s) would
            // cut off long local-LLM completions. We set HttpClient.Timeout directly instead.
            // Retries are also wrong for LLM calls (non-idempotent, partial responses).
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
            services.AddSingleton<IChatCompletionService, LocalChatService>();
            Console.WriteLine("[AI] Selected backend: Local (Ollama/OpenAI-compatible).");
            return services;
        }

        services.AddSingleton<IChatCompletionService, UnavailableChatService>();
        Console.WriteLine("[AI] Selected backend: Unavailable (missing or invalid AI configuration).");
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

        // Configure retry options from configuration section.
        // Environment variables can override via AIOptions__EmbeddingRetry__*.
        services.AddOptions<EmbeddingRetryOptions>()
            .Bind(configuration.GetSection(EmbeddingRetryOptions.SectionPath))
            .ValidateDataAnnotations()
            .Validate(options =>
            {
                options.Validate();
                return true;
            }, "Embedding retry configuration is invalid.");

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

    private static string ResolveLocalEndpoint(AIOptions options, IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(options.LocalEndpoint))
        {
            return options.LocalEndpoint!;
        }

        return configuration.GetConnectionString("ollama-chat") ?? string.Empty;
    }

    /// <summary>
    /// Returns true if <paramref name="connectionString"/> can be parsed by
    /// <see cref="NpgsqlConnectionStringBuilder"/> and resolves to a non-empty Host.
    /// Rejects null, empty, and placeholder strings like "your-postgres-connection-string-here".
    /// </summary>
    private static bool IsValidNpgsqlConnectionString(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return false;
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            return !string.IsNullOrWhiteSpace(builder.Host);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

}
