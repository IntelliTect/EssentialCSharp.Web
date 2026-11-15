using System.Net;
using System.Reflection;
using System.Text.Json;
using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

public class VersionEndpointTests : IntegrationTestBase
{
    [Test]
    public async Task GetVersion_ReturnsRunningAssemblyBuildInfo_WithoutCaching()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();

        using HttpResponseMessage response = await client.GetAsync("/version");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response.Headers.CacheControl?.NoStore).IsTrue();
        await Assert.That(response).HasJsonContent();

        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).IsValidJsonObject();

        // Deep equality pins the whole contract that deployment checks rely on: no renamed or extra fields.
        string? expectedVersion = typeof(Program).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        using JsonDocument expected = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            version = expectedVersion,
            commit = BuildInfo.FromInformationalVersion(expectedVersion).Commit,
        }));
        using JsonDocument actual = JsonDocument.Parse(body);
        await Assert.That(actual.RootElement).IsDeepEqualTo(expected.RootElement);
    }

    [Test]
    public async Task HeadVersion_ReturnsOk()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using var request = new HttpRequestMessage(HttpMethod.Head, "/version");

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task BuildInfo_InGitHubActions_IsStampedWithCheckedOutCommit()
    {
        // Locally the stamp depends on the developer's checkout, so only enforce it in CI, where the
        // checked-out commit is GITHUB_SHA and the SDK's SourceLink stamps it into the informational version.
        if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        await Assert.That(BuildInfo.Current.Commit).IsEqualTo(Environment.GetEnvironmentVariable("GITHUB_SHA"));
    }

    [Test]
    public async Task GetVersion_IsNotRateLimited()
    {
        // Deployment checks poll /version until the new revision answers; the global limiter must not reject them.
        using HttpClient client = CreateClientWithoutAutoRedirect();

        for (int i = 0; i < 40; i++)
        {
            using HttpResponseMessage response = await client.GetAsync("/version");
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }

    [Test]
    [Arguments("1.0.0+0123456789abcdef0123456789abcdef01234567", "1.0.0+0123456789abcdef0123456789abcdef01234567", "0123456789abcdef0123456789abcdef01234567")]
    [Arguments("1.0.0", "1.0.0", null)]
    [Arguments("1.0.0+", "1.0.0+", null)]
    [Arguments(null, null, null)]
    [Arguments("", null, null)]
    public async Task FromInformationalVersion_ExtractsCommitAfterPlus(string? informationalVersion, string? expectedVersion, string? expectedCommit)
    {
        BuildInfo info = BuildInfo.FromInformationalVersion(informationalVersion);

        await Assert.That(info.Version).IsEqualTo(expectedVersion);
        await Assert.That(info.Commit).IsEqualTo(expectedCommit);
    }
}
