using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace EssentialCSharp.Web.Hosting;

/// <summary>Web-owned resource wiring shared by standalone and aggregate AppHosts.</summary>
public static class WebHostingExtensions
{
    /// <summary>Registers Web and its migrations using the caller's existing databases.</summary>
    /// <param name="builder">The executing AppHost.</param>
    /// <param name="webProjectPath">
    /// An absolute path to the Web application project in the caller's checkout. The startup migration
    /// runs <c>dotnet ef --no-build</c>, so the AppHost must build this project first, typically through a
    /// <c>ProjectReference</c> to it.
    /// </param>
    /// <param name="webDatabase">The existing SQL database connection resource.</param>
    /// <param name="vectorDatabase">The existing PostgreSQL database connection resource.</param>
    /// <returns>The Web resource, available for caller-specific configuration.</returns>
    public static IResourceBuilder<ProjectResource> AddEssentialCSharpWeb(
        this IDistributedApplicationBuilder builder,
        string webProjectPath,
        IResourceBuilder<IResourceWithConnectionString> webDatabase,
        IResourceBuilder<IResourceWithConnectionString> vectorDatabase)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(webProjectPath);
        ArgumentNullException.ThrowIfNull(webDatabase);
        ArgumentNullException.ThrowIfNull(vectorDatabase);
        if (!Path.IsPathFullyQualified(webProjectPath) || !File.Exists(webProjectPath))
        {
            throw new ArgumentException(
                $"Required Web project was not found at an absolute path: '{webProjectPath}'. " +
                "Pass the full path to EssentialCSharp.Web.csproj in the actual Web checkout.",
                nameof(webProjectPath));
        }

        if (builder.ExecutionContext.IsRunMode
            && builder.Configuration.GetValue<bool>("Parameters:UseNugetContentFeed"))
        {
            throw new InvalidOperationException(
                "Parameters:UseNugetContentFeed cannot select build-time content. Remove that AppHost setting " +
                "and set the AccessToNugetFeed environment variable to true before building and starting Aspire.");
        }

        var web = builder.AddProject("essentialcsharp-web", webProjectPath, launchProfileName: null)
            .WithReference(webDatabase, connectionName: "EssentialCSharpWebContextConnection")
            .WithReference(vectorDatabase, connectionName: "PostgresVectorStore")
            .WaitFor(webDatabase)
            .WaitFor(vectorDatabase)
            .WithHttpEndpoint()
            .WithHttpHealthCheck("/health", endpointName: "http");
        web.WithEnvironment("SiteSettings__BaseUrl", web.GetEndpoint("http"));
        if (builder.ExecutionContext.IsRunMode)
        {
            // Local only: deployed environments must keep production migrations and DataProtection policy.
            web.WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");

            var resourcesBeforeMigrations = builder.Resources.ToHashSet(ReferenceEqualityComparer.Instance);
            var migrations = web.AddEFMigrations(
                    "essentialcsharp-web-ef-migrations",
                    "EssentialCSharp.Web.Data.EssentialCSharpWebContext")
                .WithReference(webDatabase, connectionName: "EssentialCSharpWebContextConnection")
                .WaitFor(webDatabase)
                .RunDatabaseUpdateOnStart();
            IsolateSharedEnvironmentCallbacks(
                web.Resource,
                builder.Resources.Where(resource => !resourcesBeforeMigrations.Contains(resource)));
            // EF reports Running before applying migrations; only completion proves the update succeeded.
            web.WaitForCompletion(migrations);

#pragma warning disable ASPIREBROWSERLOGS001
            web.WithBrowserLogs();
#pragma warning restore ASPIREBROWSERLOGS001
        }
        return web;
    }

    /// <summary>Applies local AI policy; AppHost Azure/Foundry configuration takes precedence over Ollama.</summary>
    public static IResourceBuilder<ProjectResource> WithEssentialCSharpLocalAI(
        this IResourceBuilder<ProjectResource> web)
    {
        ArgumentNullException.ThrowIfNull(web);
        var builder = web.ApplicationBuilder;
        if (builder.ExecutionContext.IsPublishMode)
        {
            return web;
        }

        string? endpoint = builder.Configuration["AIOptions:Endpoint"];
        bool azureConfigured = !string.IsNullOrWhiteSpace(endpoint);
        // Graph selection is startup configuration, not a dashboard parameter. Keep existing saved
        // disables working so upgrading does not unexpectedly download a local model.
        bool useLocalAI = !azureConfigured
            && (builder.Configuration.GetValue<bool?>("AIOptions:UseLocalAI")
                ?? builder.Configuration.GetValue<bool?>("Parameters:UseLocalAI")
                ?? true);

        if (azureConfigured)
        {
            web.WithEnvironment("AIOptions__Endpoint", endpoint);
            // Web owns its tool permissions. Forward only the Azure connection/model/prompt settings;
            // indexed environment overrides would merge a partial tool allowlist with Web's defaults.
            foreach (string key in new[] { "ChatDeploymentName", "VectorGenerationDeploymentName", "SystemPrompt" })
            {
                string? value = builder.Configuration[$"AIOptions:{key}"];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    web.WithEnvironment($"AIOptions__{key}", value);
                }
            }
        }
        web.WithEnvironment("AIOptions__UseLocalAI", useLocalAI ? "true" : "false");

        if (useLocalAI)
        {
            var ollama = builder.AddOllama("ollama")
                .WithDataVolume()
                .WithLifetime(ContainerLifetime.Persistent);
            var chat = ollama.AddModel("ollama-chat", "qwen2.5-coder:7b");
            // Web selects its local backend only when it has both a URI endpoint and a model name; the
            // model connection string ("Endpoint=...;Model=...") alone is not a valid LocalEndpoint.
            web.WithReference(chat)
                .WithEnvironment("AIOptions__LocalEndpoint", ollama.Resource.PrimaryEndpoint)
                .WithEnvironment("AIOptions__LocalChatModel", chat.Resource.ModelName)
                .WaitFor(chat);
        }

        return web;
    }

    // AddEFMigrations copies the project's EnvironmentCallbackAnnotation instances to its hidden dotnet-ef
    // tool. Those instances cache their first evaluation, so the tool's run would replace Web's own
    // environment (including connection strings) once Web starts after migration completion. Give only the
    // resources created by AddEFMigrations equivalent but independent annotations.
    private static void IsolateSharedEnvironmentCallbacks(
        ProjectResource web, IEnumerable<IResource> migrationResources)
    {
        var webCallbacks = web.Annotations.OfType<EnvironmentCallbackAnnotation>()
            .ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var resource in migrationResources)
        {
            for (int i = 0; i < resource.Annotations.Count; i++)
            {
                if (resource.Annotations[i] is EnvironmentCallbackAnnotation shared && webCallbacks.Contains(shared))
                {
                    resource.Annotations[i] = new EnvironmentCallbackAnnotation(shared.Callback);
                }
            }
        }
    }
}
