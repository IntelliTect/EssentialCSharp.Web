using System.Net;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// The layout hands the reading-time client its data through <c>window.READING_CONTEXT</c> (content pages only)
/// and a <c>csrf-token</c> meta tag (signed-in users only).
/// </summary>
public class ReadingContextRenderingTests : IntegrationTestBase
{
    private const string ContentPage = "/hello-world";

    [Test]
    public async Task ContentPage_RendersReadingContextWithCamelCaseFields()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();

        string html = await GetOkHtmlAsync(client, ContentPage);

        using (Assert.Multiple())
        {
            await Assert.That(html).Contains("window.READING_CONTEXT = {");
            await Assert.That(html).Contains("\"isAuthenticated\":false");
            await Assert.That(html).Contains("\"pageKey\":\"hello-world\"");
            await Assert.That(html).Contains("\"pageWords\":");
            await Assert.That(html).Contains("\"chapterWords\":");
            await Assert.That(html).Contains("\"wordsBeforePage\":");
            await Assert.That(html).Contains("\"chapterStartWords\":");
            await Assert.That(html).Contains("\"bookWords\":");
        }
    }

    [Test]
    [Arguments("/about")]
    [Arguments("/Identity/Account/Login")]
    public async Task NonContentPage_DoesNotRenderReadingContext(string path)
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();

        string html = await GetOkHtmlAsync(client, path);

        await Assert.That(html).DoesNotContain("READING_CONTEXT");
    }

    [Test]
    public async Task AnonymousVisitor_GetsNoCsrfMetaAndNoAntiforgeryCookie()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();

        using HttpResponseMessage response = await client.GetAsync(ContentPage);
        string html = await response.Content.ReadAsStringAsync();
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies);

        using (Assert.Multiple())
        {
            await Assert.That(html).DoesNotContain("name=\"csrf-token\"");
            await Assert.That(cookies ?? []).DoesNotContain(c => c.Contains("Antiforgery", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Test]
    public async Task SignedInUser_GetsCsrfMetaAndAuthenticatedReadingContext()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-context");
        (string cookieName, string cookieValue) = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpRequestMessage request = new(HttpMethod.Get, ContentPage);
        McpTestHelper.AddCookie(request, cookieName, cookieValue);

        using HttpResponseMessage response = await client.SendAsync(request);
        string html = await response.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(html).Contains("name=\"csrf-token\"");
            await Assert.That(html).Contains("\"isAuthenticated\":true");
        }
    }

    private static async Task<string> GetOkHtmlAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }
}
