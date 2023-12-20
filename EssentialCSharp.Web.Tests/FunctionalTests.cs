using System.Net;

namespace EssentialCSharp.Web.Tests;

public class FunctionalTests
{
    [Theory]
    [InlineData("/", Skip = "CI Needs to get the key vault values for this to work")]
    [InlineData("/hello-world", Skip = "CI Needs to get the key vault values for this to work")]
    [InlineData("/hello-world#hello-world", Skip = "CI Needs to get the key vault values for this to work")]
    //Skip this test
    public async Task WhenTheApplicationStarts_ItCanLoadLoadPages(string relativeUrl)
    {
        using WebApplicationFactory factory = new();

        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(relativeUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
