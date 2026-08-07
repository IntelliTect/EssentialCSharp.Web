using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EssentialCSharp.Web.Tests;

public class ChatRequestValidationTests : IntegrationTestBase
{
    [Test]
    public async Task ChatStream_WhenCaptchaTokenExceedsTwoThousandCharacters_DoesNotFailModelValidation()
    {
        HttpClient client = McpTestHelper.CreateClient(Factory);

        string userId = await McpTestHelper.CreateUserAsync(Factory, "chat-validation-user");
        (string cookieName, string cookieValue) = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);

        string longCaptchaToken = new('a', 2500);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/stream")
        {
            Content = JsonContent.Create(new
            {
                message = "Hello",
                enableContextualSearch = false,
                captchaResponse = longCaptchaToken
            })
        };
        McpTestHelper.AddCookie(request, cookieName, cookieValue);

        using HttpResponseMessage response = await client.SendAsync(request);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);

        using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        await Assert.That(payload.RootElement.GetProperty("errorCode").GetString()).IsEqualTo("chat_unavailable");
    }
}
