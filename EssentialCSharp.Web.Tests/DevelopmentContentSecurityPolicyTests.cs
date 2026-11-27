using EssentialCSharp.Web.Middleware;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// The test host runs in Development, where the policy is sent report-only so violations surface
/// locally without blocking anything.
/// </summary>
public class DevelopmentContentSecurityPolicyTests : IntegrationTestBase
{
    [Test]
    public async Task Development_SendsTheReportOnlyPolicy()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();

        using HttpResponseMessage response = await client.GetAsync("/health");

        await Assert.That(response.Headers.Contains("Content-Security-Policy")).IsFalse();
        await Assert.That(response.Headers.TryGetValues("Content-Security-Policy-Report-Only", out IEnumerable<string>? values)).IsTrue();
        string policy = values!.Single();
        await Assert.That(policy).Contains("script-src");
        await Assert.That(policy).DoesNotContain(CspNonce.Placeholder);
    }
}
