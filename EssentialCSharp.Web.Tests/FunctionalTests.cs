using System.Net;

namespace EssentialCSharp.Web.Tests;

public class FunctionalTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/hello-world")]
    [InlineData("/hello-world#hello-world")]
    [InlineData("/guidelines")]
    [InlineData("/healthz")]
    public async Task WhenTheApplicationStarts_ItCanLoadLoadPages(string relativeUrl)
    {
        using WebApplicationFactory factory = new();

        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(relativeUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/guidelines?rid=test-referral-id")]
    [InlineData("/about?rid=abc123")]
    [InlineData("/hello-world?rid=user-referral")]
    public async Task WhenPagesAreAccessedWithRidParameter_TheyReturnContentSuccessfully(string relativeUrl)
    {
        using WebApplicationFactory factory = new();

        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(relativeUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Ensure the response has content (not blank)
        string content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
        
        // Verify it's actually HTML content, not just whitespace
        Assert.Contains("<html", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WhenTheApplicationStarts_NonExistingPage_GivesCorrectStatusCode()
    {
        using WebApplicationFactory factory = new();

        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/non-existing-page1234");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("/guidelines?foo=bar")]
    [InlineData("/about?someOtherParam=value")]
    public async Task WhenPagesAreAccessedWithNonRidParameters_TheyStillWork(string relativeUrl)
    {
        using WebApplicationFactory factory = new();

        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(relativeUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        string content = await response.Content.ReadAsStringAsync();
        Assert.NotEmpty(content);
        Assert.Contains("<html", content, StringComparison.OrdinalIgnoreCase);
    }
}
