using System.Reflection;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace EssentialCSharp.Web.AppHost.Tests;

public class AppHostGraphTests
{
    [Test]
    public async Task StandaloneGraph_Default_UsesPersistentLocalInfrastructureAndPreservedNames()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.EssentialCSharp_Web_AppHost>(
            ["--Parameters:UseLocalAI=false"]);

        var names = builder.Resources.Select(r => r.Name).ToList();
        foreach (string expected in new[]
        {
            "sqlserver", "postgres", "EssentialCSharpWebContextConnection", "PostgresVectorStore",
            "essentialcsharp-web", "essentialcsharp-web-ef-migrations", "UseNugetContentFeed",
        })
        {
            await Assert.That(names).Contains(expected);
        }

        foreach (var container in builder.Resources.OfType<ContainerResource>().Where(r => r.Name is "sqlserver" or "postgres"))
        {
            await Assert.That(container.Annotations.OfType<ContainerMountAnnotation>().Any()).IsTrue();
            await Assert.That(container.Annotations.OfType<ContainerLifetimeAnnotation>().Single().Lifetime)
                .IsEqualTo(ContainerLifetime.Persistent);
        }

        var postgresImage = builder.Resources.Single(r => r.Name == "postgres").Annotations.OfType<ContainerImageAnnotation>().Last();
        await Assert.That(postgresImage.Image).IsEqualTo("pgvector/pgvector");
        await Assert.That(postgresImage.Tag).IsEqualTo("pg17");

        var web = builder.Resources.Single(r => r.Name == "essentialcsharp-web");
        var env = await GraphInspection.GetEnvironmentAsync(web, builder.ExecutionContext);
        await Assert.That(((ParameterResource)env["AccessToNugetFeed"]).Name).IsEqualTo("UseNugetContentFeed");
        await Assert.That(env["AIOptions__UseLocalAI"]).IsEqualTo("false");
    }

    [Test]
    public async Task StandaloneGraph_EphemeralDatabases_RemovesVolumesAndPersistence()
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.EssentialCSharp_Web_AppHost>(
            ["--Parameters:UseLocalAI=false", "--EssentialCSharpWeb:EphemeralDatabases=true"]);

        foreach (var container in builder.Resources.OfType<ContainerResource>().Where(r => r.Name is "sqlserver" or "postgres"))
        {
            await Assert.That(container.Annotations.OfType<ContainerMountAnnotation>().Any()).IsFalse();
            await Assert.That(container.Annotations.OfType<ContainerLifetimeAnnotation>()
                .All(a => a.Lifetime == ContainerLifetime.Session)).IsTrue();
        }
    }

    [Test]
    public async Task StandaloneAppHost_UserSecretsId_IsNotSharedWebId()
    {
        var appHost = System.Reflection.Assembly.Load("EssentialCSharp.Web.AppHost");

        string? id = appHost.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;

        await Assert.That(id).IsEqualTo("essentialcsharp-web-standalone-apphost");
    }
}

internal static class GraphInspection
{
    public static string WebProjectPath { get; } = FindWebProjectPath();

    public static IDistributedApplicationBuilder CreateBuilder(params string[] args) =>
        DistributedApplication.CreateBuilder(new DistributedApplicationOptions
        {
            Args = args,
            DisableDashboard = true,
            AssemblyName = typeof(GraphInspection).Assembly.GetName().Name,
        });

    public static async Task<Dictionary<string, object>> GetEnvironmentAsync(
        IResource resource, DistributedApplicationExecutionContext executionContext)
    {
        var context = new EnvironmentCallbackContext(executionContext);
        foreach (var annotation in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            await annotation.Callback(context);
        }

        return context.EnvironmentVariables;
    }

    private static string FindWebProjectPath()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "EssentialCSharp.Web", "EssentialCSharp.Web.csproj");
            if (File.Exists(Path.Combine(directory.FullName, "EssentialCSharp.Web.slnx")) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not locate EssentialCSharp.Web.csproj above the test output.");
    }
}
