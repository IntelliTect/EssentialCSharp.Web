using System.Net;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using TUnit.Aspire;

namespace EssentialCSharp.Web.AppHost.Tests;

/// <summary>Runs only the Web repository's AppHost with fresh databases and no model or private feed.</summary>
public class StandaloneWebFixture : AspireFixture<Projects.EssentialCSharp_Web_AppHost>
{
    protected override string[] Args =>
    [
        "--AIOptions:UseLocalAI=false",
        "--EssentialCSharpWeb:EphemeralDatabases=true",
    ];

    protected override TimeSpan ResourceTimeout => TimeSpan.FromMinutes(10);

    // The EF migration resource finishes rather than reporting health; Web itself waits for its completion.
    protected override ResourceWaitBehavior WaitBehavior => ResourceWaitBehavior.Named;

    protected override IEnumerable<string> ResourcesToWaitFor() => ["essentialcsharp-web"];
}

[Category("Docker")]
[RequiresLinuxContainers]
[ClassDataSource<StandaloneWebFixture>(Shared = SharedType.PerTestSession)]
public class StandaloneWebSmokeTests(StandaloneWebFixture fixture)
{
    [Test]
    public async Task FreshStart_Migrations_FinishBeforeWebIsHealthy()
    {
        var notifications = fixture.App.Services.GetRequiredService<ResourceNotificationService>();

        bool found = notifications.TryGetCurrentState("essentialcsharp-web-ef-migrations", out var migration);

        // Web is healthy only after WaitForCompletion released it, so the migration result is already final.
        await Assert.That(found).IsTrue();
        await Assert.That(migration!.Snapshot.State?.Text).IsEqualTo(KnownResourceStates.Finished);
    }

    [Test]
    [Arguments("/health")]
    [Arguments("/alive")]
    public async Task HealthEndpoint_FreshStart_ReturnsOk(string path)
    {
        using var client = fixture.CreateHttpClient("essentialcsharp-web", "http");

        using var response = await client.GetAsync(path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task SecondAppHost_DynamicPorts_RunsAlongsideFirst()
    {
        await using var second = new StandaloneWebFixture();
        await second.InitializeAsync();
        using var firstClient = fixture.CreateHttpClient("essentialcsharp-web", "http");
        using var secondClient = second.CreateHttpClient("essentialcsharp-web", "http");

        await Assert.That(secondClient.BaseAddress).IsNotEqualTo(firstClient.BaseAddress);
        using var firstResponse = await firstClient.GetAsync("/health");
        using var secondResponse = await secondClient.GetAsync("/health");
        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task PlaceholderChapter_FreshStart_RendersRepositoryContent()
    {
        using var client = fixture.CreateHttpClient("essentialcsharp-web", "http");

        using var response = await client.GetAsync("/hello-world");
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).Contains("Hello, World");
    }
}
