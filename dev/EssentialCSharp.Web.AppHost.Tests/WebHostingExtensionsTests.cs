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
        await Assert.That(endpoint.TargetPort).IsEqualTo(5000);

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
    public async Task AddEssentialCSharpWeb_PublishMode_DoesNotForceDevelopmentEnvironment()
    {
        var builder = GraphInspection.CreateBuilder(
            "--operation", "publish", "--publisher", "manifest", "--output-path", Path.GetTempPath());
        var (sql, vector) = AddSuppliedDatabases(builder);

        var web = builder.AddEssentialCSharpWeb(GraphInspection.WebProjectPath, sql, vector);

        await Assert.That(builder.ExecutionContext.IsPublishMode).IsTrue();
        var env = await GraphInspection.GetEnvironmentAsync(web.Resource, builder.ExecutionContext);
        await Assert.That(env.Keys).DoesNotContain("ASPNETCORE_ENVIRONMENT");
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
    [Arguments(new string[0], "false")]
    [Arguments(new[] { "--Parameters:UseNugetContentFeed=true" }, "true")]
    public async Task AddEssentialCSharpContentFeedParameter_Configuration_ResolvesSameReusableHandle(
        string[] args, string expected)
    {
        var builder = GraphInspection.CreateBuilder(args);

        var feed = builder.AddEssentialCSharpContentFeedParameter();

        await Assert.That(feed.Resource.Name).IsEqualTo("UseNugetContentFeed");
        await Assert.That(builder.Resources.OfType<ParameterResource>().Single(p => p.Name == "UseNugetContentFeed"))
            .IsSameReferenceAs(feed.Resource);
        await Assert.That(await feed.Resource.GetValueAsync(CancellationToken.None)).IsEqualTo(expected);
    }

    [Test]
    public async Task WithEssentialCSharpLocalAI_AzureEndpointConfigured_TakesPrecedenceOverOllama()
    {
        var builder = GraphInspection.CreateBuilder(
            "--AIOptions:Endpoint=https://example.openai.azure.com/",
            "--AIOptions:ChatDeploymentName=chat",
            "--Parameters:UseLocalAI=true");
        var (sql, vector) = AddSuppliedDatabases(builder);

        var web = builder.AddEssentialCSharpWeb(GraphInspection.WebProjectPath, sql, vector).WithEssentialCSharpLocalAI();

        var env = await GraphInspection.GetEnvironmentAsync(web.Resource, builder.ExecutionContext);
        await Assert.That(env["AIOptions__UseLocalAI"]).IsEqualTo("false");
        await Assert.That(env["AIOptions__Endpoint"]).IsEqualTo("https://example.openai.azure.com/");
        await Assert.That(env["AIOptions__ChatDeploymentName"]).IsEqualTo("chat");
        await Assert.That(builder.Resources.Any(r => r.Name is "ollama" or "UseLocalAI")).IsFalse();
    }

    [Test]
    [Arguments(new[] { "--Parameters:UseLocalAI=false" }, false)]
    [Arguments(new string[0], true)]
    public async Task WithEssentialCSharpLocalAI_UseLocalAIParameter_ControlsOllama(string[] args, bool expectOllama)
    {
        var builder = GraphInspection.CreateBuilder(args);
        var (sql, vector) = AddSuppliedDatabases(builder);

        var web = builder.AddEssentialCSharpWeb(GraphInspection.WebProjectPath, sql, vector).WithEssentialCSharpLocalAI();

        var env = await GraphInspection.GetEnvironmentAsync(web.Resource, builder.ExecutionContext);
        await Assert.That(env["AIOptions__UseLocalAI"]).IsEqualTo(expectOllama ? "true" : "false");
        await Assert.That(builder.Resources.Any(r => r.Name == "ollama")).IsEqualTo(expectOllama);
        await Assert.That(builder.Resources.Any(r => r.Name == "ollama-chat")).IsEqualTo(expectOllama);
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

    private static (IResourceBuilder<IResourceWithConnectionString> Sql, IResourceBuilder<IResourceWithConnectionString> Vector)
        AddSuppliedDatabases(IDistributedApplicationBuilder builder) =>
        (builder.AddConnectionString("EssentialCSharpWebContextConnection"),
            builder.AddConnectionString("PostgresVectorStore"));
}
