using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using EssentialCSharp.Web.Hosting;

namespace EssentialCSharp.Web.AppHost.Tests;

public class WebHostingExtensionsTests
{
    [Test]
    public async Task AddEssentialCSharpWeb_SuppliedDatabases_WiresWebWithoutCreatingInfrastructure()
    {
        var builder = GraphInspection.CreateBuilder();
        var (sql, vector) = AddSuppliedDatabases(builder);

        var web = builder.AddEssentialCSharpWeb(GraphInspection.WebProjectPath, sql, vector);

        await Assert.That(web.Resource.Name).IsEqualTo("essentialcsharp-web");
        await Assert.That(builder.Resources.Any(r => r.Name is "sqlserver" or "postgres")).IsFalse();
        // EFMigrationResource is modeled as a container; no other container infrastructure may be created.
        await Assert.That(builder.Resources.OfType<ContainerResource>().Select(r => r.Name))
            .IsEquivalentTo(["essentialcsharp-web-ef-migrations"]);
        await Assert.That(builder.Resources.Select(r => r.Name)).Contains("essentialcsharp-web-ef-migrations");

        var endpoint = web.Resource.Annotations.OfType<EndpointAnnotation>().Single(e => e.Name == "http");
        await Assert.That(endpoint.Port).IsNull();
        await Assert.That(endpoint.TargetPort).IsNull();
        await Assert.That(endpoint.IsProxied).IsTrue();

        var env = await GraphInspection.GetEnvironmentAsync(web.Resource, builder.ExecutionContext);
        await Assert.That(env["ASPNETCORE_ENVIRONMENT"]).IsEqualTo("Development");
        await Assert.That(((ConnectionStringReference)env["ConnectionStrings__EssentialCSharpWebContextConnection"]).Resource)
            .IsSameReferenceAs(sql.Resource);
        await Assert.That(((ConnectionStringReference)env["ConnectionStrings__PostgresVectorStore"]).Resource)
            .IsSameReferenceAs(vector.Resource);
        var baseUrl = (EndpointReference)env["SiteSettings__BaseUrl"];
        await Assert.That(baseUrl.Resource).IsSameReferenceAs(web.Resource);
        await Assert.That(baseUrl.EndpointName).IsEqualTo("http");
    }

    [Test]
    public async Task AddEssentialCSharpWeb_Migrations_WebWaitsForSuccessfulCompletion()
    {
        var builder = GraphInspection.CreateBuilder();
        var (sql, vector) = AddSuppliedDatabases(builder);

        var web = builder.AddEssentialCSharpWeb(GraphInspection.WebProjectPath, sql, vector);

        var migrations = builder.Resources.Single(r => r.Name == "essentialcsharp-web-ef-migrations");
        var migrationWait = web.Resource.Annotations.OfType<WaitAnnotation>().Single(w => w.Resource == migrations);
        await Assert.That(migrationWait.WaitType).IsEqualTo(WaitType.WaitForCompletion);
        await Assert.That(migrationWait.ExitCode).IsEqualTo(0);
        var migrationEnv = await GraphInspection.GetEnvironmentAsync(migrations, builder.ExecutionContext);
        await Assert.That(((ConnectionStringReference)migrationEnv["ConnectionStrings__EssentialCSharpWebContextConnection"]).Resource)
            .IsSameReferenceAs(sql.Resource);
    }

    [Test]
    public async Task AddEssentialCSharpWeb_MigrationToolEnvironment_DoesNotShareWebCallbackCache()
    {
        var builder = GraphInspection.CreateBuilder();
        var (sql, vector) = AddSuppliedDatabases(builder);

        var web = builder.AddEssentialCSharpWeb(GraphInspection.WebProjectPath, sql, vector);

        var webCallbacks = web.Resource.Annotations.OfType<EnvironmentCallbackAnnotation>()
            .ToHashSet(ReferenceEqualityComparer.Instance);
        var tool = builder.Resources.Single(r => r.Name == "ef-tool-essentialcsharp-web-ef-migrations");
        await Assert.That(tool.Annotations.OfType<EnvironmentCallbackAnnotation>().Any(webCallbacks.Contains)).IsFalse();

        // The tool still receives equivalent Web environment through independent annotations.
        var toolEnv = await GraphInspection.GetEnvironmentAsync(tool, builder.ExecutionContext);
        await Assert.That(toolEnv.Keys).Contains("ConnectionStrings__EssentialCSharpWebContextConnection");
        await Assert.That(toolEnv.Keys).Contains("ConnectionStrings__PostgresVectorStore");
    }

    [Test]
    public async Task AddEssentialCSharpWeb_PublishMode_ProducesManifestWithoutDevelopmentResources()
    {
        var outputDirectory = Directory.CreateTempSubdirectory("essentialcsharp-manifest-");
        try
        {
            string manifestPath = Path.Combine(outputDirectory.FullName, "manifest.json");
            var builder = GraphInspection.CreateBuilder(
                "--operation", "publish", "--publisher", "manifest", "--output-path", manifestPath);
            var (sql, vector) = AddSuppliedDatabases(builder);

            var web = builder.AddEssentialCSharpWeb(GraphInspection.WebProjectPath, sql, vector)
                .WithEssentialCSharpLocalAI();

            await Assert.That(builder.ExecutionContext.IsPublishMode).IsTrue();
            await Assert.That(builder.Resources.Select(r => r.Name))
                .IsEquivalentTo(["supplied-sql", "supplied-vector", "essentialcsharp-web"]);
            await Assert.That(web.Resource.Annotations.OfType<WaitAnnotation>()
                .Any(wait => wait.WaitType == WaitType.WaitForCompletion)).IsFalse();
            var env = await GraphInspection.GetEnvironmentAsync(web.Resource, builder.ExecutionContext);
            await Assert.That(env.Keys).DoesNotContain("ASPNETCORE_ENVIRONMENT");
            await Assert.That(env.Keys).DoesNotContain("AIOptions__UseLocalAI");

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await using (var app = builder.Build())
            {
                await app.RunAsync(timeout.Token);
            }

            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
            var resources = manifest.RootElement.GetProperty("resources");
            await Assert.That(resources.EnumerateObject().Select(resource => resource.Name))
                .IsEquivalentTo(["supplied-sql", "supplied-vector", "essentialcsharp-web"]);
        }
        finally
        {
            outputDirectory.Delete(recursive: true);
        }
    }

    [Test]
    [Arguments("EssentialCSharp.Web.csproj")]
    [Arguments(@"Z:\missing\EssentialCSharp.Web\EssentialCSharp.Web.csproj")]
    public async Task AddEssentialCSharpWeb_PathNotAbsoluteExistingFile_ThrowsArgumentException(string path)
    {
        var builder = GraphInspection.CreateBuilder();
        var (sql, vector) = AddSuppliedDatabases(builder);

        await Assert.That(() => builder.AddEssentialCSharpWeb(path, sql, vector))
            .Throws<ArgumentException>()
            .WithParameterName("webProjectPath");
    }

    [Test]
    public async Task AddEssentialCSharpWeb_RetiredContentFeedParameter_ExplainsBuildTimeSelection()
    {
        var builder = GraphInspection.CreateBuilder("--Parameters:UseNugetContentFeed=true");
        var (sql, vector) = AddSuppliedDatabases(builder);

        await Assert.That(() => builder.AddEssentialCSharpWeb(GraphInspection.WebProjectPath, sql, vector))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("AccessToNugetFeed environment variable");
    }

    [Test]
    public async Task WithEssentialCSharpLocalAI_AzureEndpointConfigured_TakesPrecedenceOverOllama()
    {
        var builder = GraphInspection.CreateBuilder(
            "--AIOptions:Endpoint=https://example.openai.azure.com/",
            "--AIOptions:ChatDeploymentName=chat",
            "--AIOptions:AllowedMcpTools:0=SearchBook",
            "--AIOptions:UseLocalAI=true");
        var (sql, vector) = AddSuppliedDatabases(builder);

        var web = builder.AddEssentialCSharpWeb(GraphInspection.WebProjectPath, sql, vector).WithEssentialCSharpLocalAI();

        var env = await GraphInspection.GetEnvironmentAsync(web.Resource, builder.ExecutionContext);
        await Assert.That(env["AIOptions__UseLocalAI"]).IsEqualTo("false");
        await Assert.That(env["AIOptions__Endpoint"]).IsEqualTo("https://example.openai.azure.com/");
        await Assert.That(env["AIOptions__ChatDeploymentName"]).IsEqualTo("chat");
        await Assert.That(env.Keys).DoesNotContain("AIOptions__AllowedMcpTools__0");
        await Assert.That(builder.Resources.Any(r => r.Name is "ollama" or "UseLocalAI")).IsFalse();
    }

    [Test]
    [Arguments(new[] { "--AIOptions:UseLocalAI=false" }, false)]
    [Arguments(new[] { "--Parameters:UseLocalAI=false" }, false)]
    [Arguments(new[] { "--AIOptions:UseLocalAI=false", "--Parameters:UseLocalAI=true" }, false)]
    [Arguments(new string[0], true)]
    public async Task WithEssentialCSharpLocalAI_StartupConfiguration_ControlsOllamaWithoutDashboardParameter(
        string[] args, bool expectOllama)
    {
        var builder = GraphInspection.CreateBuilder(args);
        var (sql, vector) = AddSuppliedDatabases(builder);

        var web = builder.AddEssentialCSharpWeb(GraphInspection.WebProjectPath, sql, vector).WithEssentialCSharpLocalAI();

        var env = await GraphInspection.GetEnvironmentAsync(web.Resource, builder.ExecutionContext);
        await Assert.That(env["AIOptions__UseLocalAI"]).IsEqualTo(expectOllama ? "true" : "false");
        await Assert.That(builder.Resources.Any(r => r.Name == "ollama")).IsEqualTo(expectOllama);
        await Assert.That(builder.Resources.Any(r => r.Name == "ollama-chat")).IsEqualTo(expectOllama);
        await Assert.That(builder.Resources.Any(r => r.Name == "UseLocalAI")).IsFalse();
        if (expectOllama)
        {
            var ollama = builder.Resources.Single(r => r.Name == "ollama");
            var localEndpoint = (EndpointReference)env["AIOptions__LocalEndpoint"];
            await Assert.That(localEndpoint.Resource).IsSameReferenceAs(ollama);
            await Assert.That(localEndpoint.Scheme).IsEqualTo("http");
            await Assert.That(env["AIOptions__LocalChatModel"]).IsEqualTo("qwen2.5-coder:7b");
        }
        else
        {
            await Assert.That(env.ContainsKey("AIOptions__LocalEndpoint")).IsFalse();
            await Assert.That(env.ContainsKey("AIOptions__LocalChatModel")).IsFalse();
        }
    }

    [Test]
    public async Task WithEssentialCSharpLocalAI_InvalidStartupConfiguration_DoesNotSilentlyEnableDownloads()
    {
        var builder = GraphInspection.CreateBuilder("--AIOptions:UseLocalAI=invalid");
        var (sql, vector) = AddSuppliedDatabases(builder);
        var web = builder.AddEssentialCSharpWeb(GraphInspection.WebProjectPath, sql, vector);

        await Assert.That(() => web.WithEssentialCSharpLocalAI())
            .Throws<InvalidOperationException>();
        await Assert.That(builder.Resources.Any(r => r.Name == "ollama")).IsFalse();
    }

    private static (IResourceBuilder<IResourceWithConnectionString> Sql, IResourceBuilder<IResourceWithConnectionString> Vector)
        AddSuppliedDatabases(IDistributedApplicationBuilder builder) =>
        (builder.AddConnectionString("supplied-sql"),
            builder.AddConnectionString("supplied-vector"));
}
