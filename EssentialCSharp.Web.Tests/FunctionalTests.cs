﻿using System.Net;

namespace EssentialCSharp.Web.Tests;

public class FunctionalTests
{
    [Theory]
    [InlineData("/")]
    public async Task WhenTheApplicationStarts_ItCanLoadLoadPages(string relativeUrl)
    {
        using WebApplicationFactory factory = new();

        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync(relativeUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
