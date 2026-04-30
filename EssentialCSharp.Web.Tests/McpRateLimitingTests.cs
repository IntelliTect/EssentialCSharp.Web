using System.Net;
using System.Text.Json;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Each class gets its own factory so the global limiter starts from a fresh state.
/// </summary>
[NotInParallel("McpTests")]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class McpDistinctUserRateLimitingTests(WebApplicationFactory factory)
{
    [Test]
    public async Task DistinctValidMcpUsers_DoNotShareRateLimitBucket()
    {
        HttpClient client = McpTestHelper.CreateClient(factory);

        for (int i = 0; i < 31; i++)
        {
            (_, string rawToken) = await McpTestHelper.CreateUserAndTokenAsync(
                factory,
                $"mcp-rate-limit-isolation-{i}",
                userPrefix: $"mcp-isolation-{i}");

            using var request = McpTestHelper.CreateInitializeRequest("/mcp");
            McpTestHelper.AddBearerToken(request, rawToken);

            using HttpResponseMessage response = await client.SendAsync(request);
            await Assert.That(response.StatusCode)
                .IsEqualTo(HttpStatusCode.OK)
                .Because($"distinct MCP user request {i + 1} should use its own rate-limit bucket");
        }
    }
}

[NotInParallel("McpTests")]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class McpPerUserRateLimitingTests(WebApplicationFactory factory)
{
    [Test]
    public async Task SingleValidMcpUser_ExceedingTokenBucket_Returns429AndDoesNotCountRejectedRequests()
    {
        (_, string rawToken) = await McpTestHelper.CreateUserAndTokenAsync(
            factory,
            "mcp-rate-limit-single-user",
            userPrefix: "mcp-single-user");
        HttpClient client = McpTestHelper.CreateClient(factory);
        List<HttpStatusCode> statuses = [];
        string? rateLimitedPayload = null;
        string? rateLimitedContentType = null;
        TimeSpan? retryAfter = null;
        int totalRequests = McpRateLimiterPolicy.AuthenticatedTokenLimit + 15;

        for (int i = 0; i < totalRequests; i++)
        {
            using var request = McpTestHelper.CreateInitializeRequest("/mcp");
            McpTestHelper.AddBearerToken(request, rawToken);

            using HttpResponseMessage response = await client.SendAsync(request);
            statuses.Add(response.StatusCode);
            if (response.StatusCode == HttpStatusCode.TooManyRequests && rateLimitedPayload is null)
            {
                rateLimitedPayload = await response.Content.ReadAsStringAsync();
                rateLimitedContentType = response.Content.Headers.ContentType?.MediaType;
                retryAfter = response.Headers.RetryAfter?.Delta;
            }
        }

        (long UsageCount, bool HasLastUsedAt) tokenUsage = factory.InServiceScope(services =>
        {
            var db = services.GetRequiredService<EssentialCSharpWebContext>();
            byte[] tokenHash = McpApiTokenService.HashToken(rawToken);
            var token = db.McpApiTokens.Single(t => t.TokenHash == tokenHash);
            return (token.UsageCount, token.LastUsedAt.HasValue);
        });

        await Assert.That(statuses.Take(McpRateLimiterPolicy.AuthenticatedTokenLimit)
            .All(status => status == HttpStatusCode.OK)).IsTrue();
        await Assert.That(statuses.Skip(McpRateLimiterPolicy.AuthenticatedTokenLimit)
            .Any(status => status == HttpStatusCode.TooManyRequests)).IsTrue();

        int successCount = statuses.Count(status => status == HttpStatusCode.OK);
        await Assert.That(successCount).IsLessThan(totalRequests);
        await Assert.That(tokenUsage.UsageCount).IsEqualTo((long)successCount);
        await Assert.That(tokenUsage.HasLastUsedAt).IsTrue();

        string payload = rateLimitedPayload
            ?? throw new InvalidOperationException("Expected at least one MCP token-bucket rejection.");
        await Assert.That(rateLimitedContentType).IsEqualTo("application/json");

        TimeSpan retryAfterDelta = retryAfter
            ?? throw new InvalidOperationException("Expected Retry-After on the MCP token-bucket rejection.");
        await Assert.That(retryAfterDelta.TotalSeconds).IsGreaterThan(0d);

        using JsonDocument document = JsonDocument.Parse(payload);
        JsonElement root = document.RootElement;
        await Assert.That(root.GetProperty("jsonrpc").GetString()).IsEqualTo("2.0");
        await Assert.That(root.GetProperty("id").ValueKind).IsEqualTo(JsonValueKind.Null);
        JsonElement error = root.GetProperty("error");
        await Assert.That(error.GetProperty("code").GetInt32()).IsEqualTo(-32000);
        await Assert.That(error.GetProperty("message").GetString()).Contains("Rate limit exceeded");
    }
}

[NotInParallel("McpTests")]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class McpAnonymousRateLimitingTests(WebApplicationFactory factory)
{
    [Test]
    public async Task InvalidMcpBearerRequests_FallBackToAnonymousIpBucket()
    {
        HttpClient client = McpTestHelper.CreateClient(factory);

        for (int i = 0; i < McpRateLimiterPolicy.AnonymousPermitLimit; i++)
        {
            using var request = McpTestHelper.CreateInitializeRequest("/mcp");
            McpTestHelper.AddBearerToken(request, "mcp_invalid_token_that_does_not_exist");

            using HttpResponseMessage response = await client.SendAsync(request);
            await Assert.That(response.StatusCode)
                .IsEqualTo(HttpStatusCode.Unauthorized)
                .Because($"invalid MCP bearer request {i + 1} should still challenge before the anonymous bucket is exhausted");
        }

        using var rateLimitedRequest = McpTestHelper.CreateInitializeRequest("/mcp");
        McpTestHelper.AddBearerToken(rateLimitedRequest, "mcp_invalid_token_that_does_not_exist");

        using HttpResponseMessage rateLimitedResponse = await client.SendAsync(rateLimitedRequest);
        await Assert.That(rateLimitedResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }
}

[NotInParallel("McpTests")]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class McpCookieIsolationRateLimitingTests(WebApplicationFactory factory)
{
    [Test]
    public async Task InvalidMcpBearerRequests_WithDifferentSiteCookies_StillShareAnonymousIpBucket()
    {
        HttpClient client = McpTestHelper.CreateClient(factory);

        for (int i = 0; i < McpRateLimiterPolicy.AnonymousPermitLimit; i++)
        {
            string cookieUserId = await McpTestHelper.CreateUserAsync(factory, $"mcp-cookie-user-{i}");
            (string cookieName, string cookieValue) = await McpTestHelper.CreateIdentityApplicationCookieAsync(factory, cookieUserId);

            using var request = McpTestHelper.CreateInitializeRequest("/mcp");
            McpTestHelper.AddBearerToken(request, "mcp_invalid_token_that_does_not_exist");
            McpTestHelper.AddCookie(request, cookieName, cookieValue);

            using HttpResponseMessage response = await client.SendAsync(request);
            await Assert.That(response.StatusCode)
                .IsEqualTo(HttpStatusCode.Unauthorized)
                .Because($"invalid MCP bearer request {i + 1} should ignore the site cookie principal and stay in the anonymous/IP bucket");
        }

        string finalCookieUserId = await McpTestHelper.CreateUserAsync(factory, "mcp-cookie-user-final");
        (string finalCookieName, string finalCookieValue) = await McpTestHelper.CreateIdentityApplicationCookieAsync(factory, finalCookieUserId);

        using var rateLimitedRequest = McpTestHelper.CreateInitializeRequest("/mcp");
        McpTestHelper.AddBearerToken(rateLimitedRequest, "mcp_invalid_token_that_does_not_exist");
        McpTestHelper.AddCookie(rateLimitedRequest, finalCookieName, finalCookieValue);

        using HttpResponseMessage rateLimitedResponse = await client.SendAsync(rateLimitedRequest);
        await Assert.That(rateLimitedResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }
}

[NotInParallel("McpTests")]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class McpGlobalBypassRateLimitingTests(WebApplicationFactory factory)
{
    [Test]
    public async Task ValidMcpPostRequests_DoNotConsumeGlobalLimiterBudgetForGetShim()
    {
        (_, string rawToken) = await McpTestHelper.CreateUserAndTokenAsync(
            factory,
            "mcp-global-bypass",
            userPrefix: "mcp-bypass");
        HttpClient client = McpTestHelper.CreateClient(factory);

        for (int i = 0; i < 10; i++)
        {
            using var postRequest = McpTestHelper.CreateInitializeRequest("/mcp");
            McpTestHelper.AddBearerToken(postRequest, rawToken);

            using HttpResponseMessage postResponse = await client.SendAsync(postRequest);
            await Assert.That(postResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        for (int i = 0; i < 30; i++)
        {
            using var getRequest = new HttpRequestMessage(HttpMethod.Get, "/mcp");
            McpTestHelper.AddBearerToken(getRequest, rawToken);
            getRequest.Headers.Accept.ParseAdd("text/event-stream");

            using HttpResponseMessage getResponse = await client.SendAsync(getRequest);
            await Assert.That(getResponse.StatusCode)
                .IsEqualTo(HttpStatusCode.MethodNotAllowed)
                .Because($"global request {i + 1} should still be within the non-MCP GET shim limit");
        }

        using var rateLimitedGetRequest = new HttpRequestMessage(HttpMethod.Get, "/mcp");
        McpTestHelper.AddBearerToken(rateLimitedGetRequest, rawToken);
        rateLimitedGetRequest.Headers.Accept.ParseAdd("text/event-stream");

        using HttpResponseMessage rateLimitedGetResponse = await client.SendAsync(rateLimitedGetRequest);
        await Assert.That(rateLimitedGetResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }
}

[NotInParallel("McpTests")]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class McpWellKnownIsolationRateLimitingTests(WebApplicationFactory factory)
{
    [Test]
    public async Task WellKnownRequests_DoNotConsumeContentLimiterBudget()
    {
        HttpClient client = McpTestHelper.CreateClient(factory);

        for (int i = 0; i < 10; i++)
        {
            using HttpResponseMessage wellKnownResponse =
                await client.GetAsync("/.well-known/oauth-protected-resource");
            await Assert.That(wellKnownResponse.StatusCode)
                .IsEqualTo(HttpStatusCode.NotFound)
                .Because($"well-known request {i + 1} should short-circuit with 404");
        }

        for (int i = 0; i < 10; i++)
        {
            using HttpResponseMessage contentResponse = await client.GetAsync("/hello-world");
            await Assert.That(contentResponse.StatusCode)
                .IsNotEqualTo(HttpStatusCode.TooManyRequests)
                .Because($"content request {i + 1} should still have its full limiter budget");
        }

        using HttpResponseMessage rateLimitedResponse = await client.GetAsync("/hello-world");
        await Assert.That(rateLimitedResponse.StatusCode).IsEqualTo(HttpStatusCode.TooManyRequests);
    }
}
