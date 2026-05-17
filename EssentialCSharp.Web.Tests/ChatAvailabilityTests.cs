using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EssentialCSharp.Web.Tests;

[NotInParallel("ChatAvailabilityTests")]
public class ChatAvailabilityTests : IntegrationTestBase
{
    private const string HCaptchaTestToken = "10000000-aaaa-bbbb-cccc-000000000001";

    [Test]
    public async Task ChatMessage_WhenBackendUnavailable_Returns503WithContract()
    {
        HttpClient client = McpTestHelper.CreateClient(Factory);

        string userId = await McpTestHelper.CreateUserAsync(Factory, "chat-unavailable");
        (string cookieName, string cookieValue) = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/message")
        {
            Content = JsonContent.Create(new { message = "Hello", enableContextualSearch = false, captchaResponse = HCaptchaTestToken })
        };
        McpTestHelper.AddCookie(request, cookieName, cookieValue);

        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);

        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.GetProperty("errorCode").GetString()).IsEqualTo("chat_unavailable");
    }

    [Test]
    public async Task ChatStream_WhenBackendUnavailable_Returns503WithContract()
    {
        HttpClient client = McpTestHelper.CreateClient(Factory);

        string userId = await McpTestHelper.CreateUserAsync(Factory, "chat-stream-unavailable");
        (string cookieName, string cookieValue) = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new { message = "Hello", enableContextualSearch = false, captchaResponse = HCaptchaTestToken })
        };
        McpTestHelper.AddCookie(request, cookieName, cookieValue);

        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);

        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.GetProperty("errorCode").GetString()).IsEqualTo("chat_unavailable");
    }
}
