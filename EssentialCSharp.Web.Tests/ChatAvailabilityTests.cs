using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EssentialCSharp.Web.Tests;

public class ChatAvailabilityTests : IntegrationTestBase
{
    private const string HCaptchaTestToken = "10000000-aaaa-bbbb-cccc-000000000001";
    private const string InvalidHCaptchaTestToken = "invalid-captcha-test-token";
    private const string UnavailableHCaptchaTestToken = "unavailable-captcha-test-token";

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

    [Test]
    [Arguments("/api/chat/message", "chat-invalid-message", InvalidHCaptchaTestToken, HttpStatusCode.Forbidden, "captcha_failed")]
    [Arguments("/api/chat/stream", "chat-invalid-stream", InvalidHCaptchaTestToken, HttpStatusCode.Forbidden, "captcha_failed")]
    [Arguments("/api/chat/message", "chat-captcha-unavailable-message", UnavailableHCaptchaTestToken, HttpStatusCode.ServiceUnavailable, "captcha_unavailable")]
    [Arguments("/api/chat/stream", "chat-captcha-unavailable-stream", UnavailableHCaptchaTestToken, HttpStatusCode.ServiceUnavailable, "captcha_unavailable")]
    public async Task ChatEndpoint_WhenCaptchaValidationFails_ReturnsExpectedStatusAndCode(
        string endpoint,
        string userNameSeed,
        string captchaResponse,
        HttpStatusCode expectedStatus,
        string expectedErrorCode)
    {
        HttpClient client = McpTestHelper.CreateClient(Factory);

        string userId = await McpTestHelper.CreateUserAsync(Factory, userNameSeed);
        (string cookieName, string cookieValue) = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { message = "Hello", enableContextualSearch = false, captchaResponse })
        };
        McpTestHelper.AddCookie(request, cookieName, cookieValue);

        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(expectedStatus);

        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.GetProperty("errorCode").GetString()).IsEqualTo(expectedErrorCode);
    }
}
