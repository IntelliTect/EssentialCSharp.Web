using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Services.Referrals;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Regression tests: every page view with ?rid= counts toward the referrer, but requests to endpoints that skip rate
/// limiting (which aren't page views) don't.
/// </summary>
public class ReferralTrackingMiddlewareTests : IntegrationTestBase
{
    [Test]
    [Arguments("/health")]
    [Arguments("/alive")]
    [Arguments("/version")]
    [Arguments("/.well-known/anything")]
    public async Task RateLimitExemptEndpoints_DoNotCountReferrals(string path)
    {
        (string userId, string referralId) = await CreateReferrerAsync();
        using HttpClient client = CreateClientWithoutAutoRedirect();

        for (int i = 0; i < 5; i++)
        {
            using HttpResponseMessage response = await client.GetAsync($"{path}?rid={referralId}");
        }

        await Assert.That(await GetReferralCountAsync(userId)).IsEqualTo(0);
    }

    [Test]
    public async Task RepeatPageViewsByTheSameVisitor_EachCount()
    {
        (string userId, string referralId) = await CreateReferrerAsync();
        using HttpClient client = CreateClientWithoutAutoRedirect();

        for (int i = 0; i < 2; i++)
        {
            using HttpResponseMessage response = await client.GetAsync($"/hello-world?rid={referralId}");
        }

        await Assert.That(await GetReferralCountAsync(userId)).IsEqualTo(2);
    }

    [Test]
    public async Task PageViewsByDifferentVisitors_EachCount()
    {
        (string userId, string referralId) = await CreateReferrerAsync();
        using HttpClient client = CreateClientWithoutAutoRedirect();

        foreach (string visitorPrefix in new[] { "visitor-a", "visitor-b" })
        {
            string visitorId = await McpTestHelper.CreateUserAsync(Factory.Services, visitorPrefix);
            (string name, string value) = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory.Services, visitorId);
            using HttpRequestMessage request = new(HttpMethod.Get, $"/hello-world?rid={referralId}");
            McpTestHelper.AddCookie(request, name, value);
            using HttpResponseMessage response = await client.SendAsync(request);
        }

        await Assert.That(await GetReferralCountAsync(userId)).IsEqualTo(2);
    }

    private async Task<(string UserId, string ReferralId)> CreateReferrerAsync()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory.Services, "referrer");
        string referralId = await InServiceScopeAsync(async services =>
        {
            EssentialCSharpWebUser user = await services.GetRequiredService<UserManager<EssentialCSharpWebUser>>().FindByIdAsync(userId)
                ?? throw new InvalidOperationException("Referrer was not created.");
            return await services.GetRequiredService<IReferralService>().EnsureReferralIdAsync(user)
                ?? throw new InvalidOperationException("No referral ID.");
        });
        return (userId, referralId);
    }

    private Task<int> GetReferralCountAsync(string userId) => InServiceScopeAsync(services =>
        services.GetRequiredService<EssentialCSharpWebContext>().Users
            .Where(u => u.Id == userId)
            .Select(u => u.ReferralCount)
            .SingleAsync());
}
