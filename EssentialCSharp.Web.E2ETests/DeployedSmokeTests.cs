using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml.Linq;

namespace EssentialCSharp.Web.E2ETests;

/// <summary>
/// Anonymous, read-only checks against a deployed site. They gate promotion to production, so they cover
/// configuration that unit and integration tests cannot see (Key Vault wiring, real content, external
/// login registration, base URL) while staying well inside the site's per-IP rate limits.
/// </summary>
[Category("Deployed")]
[RequiresDeployedSite]
public partial class DeployedSmokeTests
{
    [Test]
    [Arguments("health")]
    [Arguments("alive")]
    public async Task HealthEndpoint_ReturnsOk(string path)
    {
        using HttpClient client = DeployedSite.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(path);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task Version_ReportsDeployedCommit()
    {
        using HttpClient client = DeployedSite.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("version");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(response).HasJsonContent();
        string body = await response.Content.ReadAsStringAsync();
        await Assert.That(body).IsValidJsonObject();

        using JsonDocument payload = JsonDocument.Parse(body);
        string? commit = payload.RootElement.GetProperty("commit").GetString();
        if (DeployedSite.ExpectedCommit is { } expectedCommit)
        {
            await Assert.That(commit).IsEqualTo(expectedCommit);
        }
        else
        {
            await Assert.That(commit).IsNotNullOrWhiteSpace();
        }
    }

    [Test]
    public async Task HomePage_RendersWithCanonicalUrlOnDeployedOrigin()
    {
        using HttpClient client = DeployedSite.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("home");
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(body).Contains("Explore Essential C#");
        // SiteSettings:BaseUrl drives canonical links and the sitemap; a wrong value silently hurts SEO.
        await Assert.That(body).Contains($"<link rel=\"canonical\" href=\"{new Uri(DeployedSite.RequiredBaseUrl, "home")}\"");
    }

    [Test]
    public async Task Sitemap_ListsFullBook_AndAChapterPageRenders()
    {
        using HttpClient client = DeployedSite.CreateClient();

        using HttpResponseMessage sitemapResponse = await client.GetAsync("sitemap.xml");
        await Assert.That(sitemapResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);

        XDocument sitemap = XDocument.Parse(await sitemapResponse.Content.ReadAsStringAsync());
        List<Uri> locations = sitemap.Descendants()
            .Where(element => element.Name.LocalName == "loc")
            .Select(element => new Uri(element.Value.Trim()))
            .ToList();

        // Placeholder content (used when the content package is missing) has only a handful of pages;
        // the real book has hundreds. This catches an image built without the book content.
        await Assert.That(locations.Count).IsGreaterThan(100);
        await Assert.That(locations.All(uri => uri.Host == DeployedSite.RequiredBaseUrl.Host)).IsTrue();

        // One content page from the middle of the book (content pages share a tight per-IP rate limit).
        Uri chapterPage = locations[locations.Count / 2];
        using HttpResponseMessage pageResponse = await client.GetAsync(chapterPage.PathAndQuery.TrimStart('/'));
        string pageBody = await pageResponse.Content.ReadAsStringAsync();

        await Assert.That(pageResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(pageBody).Contains("window.TOC_DATA");
    }

    [Test]
    public async Task LoginPage_RendersCaptchaAndExternalProviders()
    {
        using HttpClient client = DeployedSite.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("Identity/Account/Login");
        string body = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(CaptchaSiteKeyPattern().IsMatch(body)).IsTrue()
            .Because("login requires hCaptcha, which fails closed when its keys are missing");
        await Assert.That(body).Contains("name=\"provider\" value=\"GitHub\"");
        await Assert.That(body).Contains("name=\"provider\" value=\"Microsoft\"");
    }

    /// <summary>
    /// Posts the external-login form without following the redirect. Verifies the provider is registered
    /// with a client id and the callback points back at this deployment; the OAuth round trip itself
    /// (secrets, consent, account linking) is out of scope for an automated smoke test.
    /// </summary>
    [Test]
    [Arguments("GitHub", "github.com", "/login/oauth/authorize", "signin-github")]
    [Arguments("Microsoft", "login.microsoftonline.com", "/common/oauth2/v2.0/authorize", "signin-microsoft")]
    public async Task ExternalLogin_RedirectsToProviderWithDeployedCallback(
        string provider, string expectedHost, string expectedPath, string expectedCallbackPath)
    {
        using HttpClient client = DeployedSite.CreateClient();

        using HttpResponseMessage loginPage = await client.GetAsync("Identity/Account/Login");
        string loginBody = await loginPage.Content.ReadAsStringAsync();
        Match token = AntiforgeryTokenPattern().Match(loginBody);
        await Assert.That(token.Success).IsTrue();

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["provider"] = provider,
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups["token"].Value),
        });
        using HttpResponseMessage response = await client.PostAsync("Identity/Account/ExternalLogin", form);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        Uri location = await Assert.That(response.Headers.Location).IsNotNull();
        await Assert.That(location.Host).IsEqualTo(expectedHost);
        await Assert.That(location.AbsolutePath).IsEqualTo(expectedPath);

        var query = HttpUtility.ParseQueryString(location.Query);
        await Assert.That(query["client_id"]).IsNotNullOrWhiteSpace();
        await Assert.That(query["redirect_uri"]).IsEqualTo(new Uri(DeployedSite.RequiredBaseUrl, expectedCallbackPath).ToString());
    }

    [GeneratedRegex("(?:data-sitekey=\"|siteKey:\\s*\")(?<key>[^\"]+)\"")]
    private static partial Regex CaptchaSiteKeyPattern();

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"(?<token>[^\"]+)\"")]
    private static partial Regex AntiforgeryTokenPattern();
}
