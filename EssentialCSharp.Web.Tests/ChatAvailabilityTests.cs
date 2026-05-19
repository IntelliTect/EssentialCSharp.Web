using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EssentialCSharp.Web.Tests;

public class ChatAvailabilityTests : IntegrationTestBase
{
    private const string HCaptchaTestToken = "10000000-aaaa-bbbb-cccc-000000000001";

    [Test]
    [Arguments("/api/chat/message", "chat-unavailable-message")]
    [Arguments("/api/chat/stream", "chat-unavailable-stream")]
    public async Task ChatEndpoint_WhenBackendUnavailable_Returns503WithContract(string endpoint, string userNameSeed)
    {
        HttpClient client = McpTestHelper.CreateClient(Factory);

        string userId = await McpTestHelper.CreateUserAsync(Factory, userNameSeed);
        (string cookieName, string cookieValue) = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
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
