using System.Net;
using System.Text.RegularExpressions;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Regression tests: AI chat history kept in browser storage must not carry over to the next account that signs in
/// on the same browser. Logout clears site storage, and each page tells the chat widget whose state it may reuse.
/// </summary>
public partial class ChatStorageIsolationTests : IntegrationTestBase
{
    [GeneratedRegex("window\\.CHAT_STORAGE_OWNER = (null|\"[0-9A-F]{32}\");")]
    private static partial Regex StorageOwnerScript();

    private static async Task<string> GetStorageOwnerAsync(HttpClient client, (string Name, string Value)? cookie)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, "/home");
        if (cookie is { } c)
        {
            McpTestHelper.AddCookie(request, c.Name, c.Value);
        }
        using HttpResponseMessage response = await client.SendAsync(request);
        Match match = StorageOwnerScript().Match(await response.Content.ReadAsStringAsync());
        if (!match.Success)
        {
            throw new InvalidOperationException($"CHAT_STORAGE_OWNER not found on /home ({(int)response.StatusCode}).");
        }
        return match.Groups[1].Value;
    }

    [Test]
    public async Task Logout_ClearsSiteStorage()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "chat-logout");
        (string Name, string Value) cookie = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);
        using HttpClient client = CreateClientWithoutAutoRedirect();

        using HttpRequestMessage page = new(HttpMethod.Get, "/Identity/Account/Logout");
        McpTestHelper.AddCookie(page, cookie.Name, cookie.Value);
        using HttpResponseMessage pageResponse = await client.SendAsync(page);
        string html = await pageResponse.Content.ReadAsStringAsync();
        string token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;
        string antiforgeryCookie = pageResponse.Headers.GetValues("Set-Cookie")
            .Select(v => v.Split(';')[0])
            .First(v => v.StartsWith(".AspNetCore.Antiforgery.", StringComparison.Ordinal));

        using HttpRequestMessage logout = new(HttpMethod.Post, "/Identity/Account/Logout")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token })
        };
        logout.Headers.Add("Cookie", $"{cookie.Name}={cookie.Value}; {antiforgeryCookie}");
        using HttpResponseMessage response = await client.SendAsync(logout);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(response.Headers.TryGetValues("Clear-Site-Data", out IEnumerable<string>? values)).IsTrue();
            await Assert.That(string.Join(",", values ?? [])).IsEqualTo("\"storage\"");
        }
    }

    [Test]
    public async Task Pages_ExposeAStorageOwnerThatDiffersPerUserAndIsNullWhenSignedOut()
    {
        string userA = await McpTestHelper.CreateUserAsync(Factory, "chat-owner-a");
        string userB = await McpTestHelper.CreateUserAsync(Factory, "chat-owner-b");
        (string Name, string Value) cookieA = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userA);
        (string Name, string Value) cookieB = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userB);
        using HttpClient client = CreateClientWithoutAutoRedirect();

        string ownerA = await GetStorageOwnerAsync(client, cookieA);
        string ownerAAgain = await GetStorageOwnerAsync(client, cookieA);
        string ownerB = await GetStorageOwnerAsync(client, cookieB);
        string anonymous = await GetStorageOwnerAsync(client, null);

        using (Assert.Multiple())
        {
            await Assert.That(ownerA).IsNotEqualTo("null");
            await Assert.That(ownerA).IsEqualTo(ownerAAgain);
            await Assert.That(ownerB).IsNotEqualTo(ownerA);
            await Assert.That(anonymous).IsEqualTo("null");
            await Assert.That(ownerA).DoesNotContain(userA);
        }
    }
}
