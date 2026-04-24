using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// HTTP integration tests for the "content" rate limit policy.
/// Uses its own factory (PerClass) to get a fresh in-memory rate limiter for each run.
/// Anonymous users are limited to 10 requests per minute on chapter content pages.
/// </summary>
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class ContentRateLimitingTests(WebApplicationFactory factory)
{
    [Test]
    public async Task ContentEndpoint_ExceedingPerMinuteLimit_Returns429()
    {
        // AllowAutoRedirect = false prevents redirect-following from consuming extra permits.
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        // Anonymous limit is 10/min. First 10 requests should not be rate-limited.
        for (int i = 0; i < 10; i++)
        {
            using HttpResponseMessage response = await client.GetAsync("/hello-world");
            await Assert.That(response.StatusCode)
                .IsNotEqualTo(HttpStatusCode.TooManyRequests)
                .Because($"request {i + 1} of 10 should be within the rate limit");
        }

        // 11th request must be rejected by the content rate limiter.
        using HttpResponseMessage rateLimited = await client.GetAsync("/hello-world");
        await Assert.That(rateLimited.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }
}
