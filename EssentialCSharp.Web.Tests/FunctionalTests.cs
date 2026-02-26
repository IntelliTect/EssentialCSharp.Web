using System.Net;

namespace EssentialCSharp.Web.Tests;

[NotInParallel("FunctionalTests")]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class FunctionalTests(WebApplicationFactory factory)
{
    [Test]
    [Arguments("/")]
    [Arguments("/hello-world")]
    [Arguments("/hello-world#hello-world")]
    [Arguments("/guidelines")]
    [Arguments("/healthz")]
    public async Task WhenTheApplicationStarts_ItCanLoadLoadPages(string relativeUrl)
    {
        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(relativeUrl);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments("/guidelines?rid=test-referral-id")]
    [Arguments("/about?rid=abc123")]
    [Arguments("/hello-world?rid=user-referral")]
    [Arguments("/guidelines?rid=")]
    [Arguments("/about?rid=   ")]
    [Arguments("/guidelines?foo=bar")]
    [Arguments("/about?someOtherParam=value")]
    public async Task WhenPagesAreAccessed_TheyReturnHtml(string relativeUrl)
    {
        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(relativeUrl);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Ensure the response has content (not blank)
        string content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsNotEmpty();

        // Verify it's actually HTML content, not just whitespace
        await Assert.That(content).Contains("<html", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    public async Task WhenTheApplicationStarts_NonExistingPage_GivesCorrectStatusCode()
    {
        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/non-existing-page1234");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
    }
}