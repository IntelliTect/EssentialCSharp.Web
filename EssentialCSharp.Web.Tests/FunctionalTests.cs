using System.Net;

namespace EssentialCSharp.Web.Tests;

public class FunctionalTests
{
    [Theory]
    [InlineData("/")]
    [InlineData("/hello-world")]
    [InlineData("/hello-world#hello-world")]
    [InlineData("/guidelines")]
    public async Task WhenTheApplicationStarts_ItCanLoadLoadPages(string relativeUrl)
    {
        using WebApplicationFactory factory = new();

        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(relativeUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task WhenTheApplicationStarts_NonExistingPage_GivesCorrectStatusCode()
    {
        using WebApplicationFactory factory = new();

        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/non-existing-page1234");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
