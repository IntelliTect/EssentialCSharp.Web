using System.Net;
using System.Net.Http.Json;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Regression tests: the configured <c>ChatThrottling</c> per-window message cap is enforced per user, in addition to
/// the per-minute burst limit. It used to exist only in configuration.
/// </summary>
public class ChatRateLimitTests : IntegrationTestBase
{
    private const string HCaptchaTestToken = "10000000-aaaa-bbbb-cccc-000000000001";

    private static async Task<HttpStatusCode> SendChatMessageAsync(HttpClient client, (string Name, string Value) cookie)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, "/api/chat/message")
        {
            Content = JsonContent.Create(new { message = "Hello", enableContextualSearch = false, captchaResponse = HCaptchaTestToken })
        };
        McpTestHelper.AddCookie(request, cookie.Name, cookie.Value);
        using HttpResponseMessage response = await client.SendAsync(request);
        return response.StatusCode;
    }

    [Test]
    public async Task ChatMessages_BeyondTheConfiguredWindowCap_AreRejectedWith429()
    {
        WebApplicationFactory<Program> factory = Factory.Inner.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<ChatThrottlingOptions>(options => options.MaxMessagesPerWindow = 3)));
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        string userId = await McpTestHelper.CreateUserAsync(factory.Services, "chat-hourly-cap");
        (string Name, string Value) cookie = await McpTestHelper.CreateIdentityApplicationCookieAsync(factory.Services, userId);
        string otherUserId = await McpTestHelper.CreateUserAsync(factory.Services, "chat-hourly-cap-other");
        (string Name, string Value) otherCookie = await McpTestHelper.CreateIdentityApplicationCookieAsync(factory.Services, otherUserId);

        List<HttpStatusCode> statuses = [];
        for (int i = 0; i < 4; i++)
        {
            statuses.Add(await SendChatMessageAsync(client, cookie));
        }
        HttpStatusCode otherUser = await SendChatMessageAsync(client, otherCookie);

        using (Assert.Multiple())
        {
            // The test chat backend is unavailable, so permitted requests return 503.
            await Assert.That(statuses.Take(3)).DoesNotContain(HttpStatusCode.TooManyRequests);
            await Assert.That(statuses[3]).IsEqualTo(HttpStatusCode.TooManyRequests);
            await Assert.That(otherUser).IsNotEqualTo(HttpStatusCode.TooManyRequests);
        }
    }
}
