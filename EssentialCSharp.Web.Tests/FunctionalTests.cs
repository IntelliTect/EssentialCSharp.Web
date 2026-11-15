using System.Net;
using HtmlAgilityPack;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

public class FunctionalTests : IntegrationTestBase
{
    [Test]
    [Arguments("/")]
    [Arguments("/hello-world")]
    [Arguments("/hello-world#hello-world")]
    [Arguments("/guidelines")]
    [Arguments("/health")]
    [Arguments("/alive")]
    public async Task WhenTheApplicationStarts_ItCanLoadLoadPages(string relativeUrl)
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, relativeUrl);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    [Arguments("/guidelines?rid=test-referral-id")]
    [Arguments("/about?rid=abc123")]
    [Arguments("/hello-world?rid=user-referral")]
    [Arguments("/guidelines?rid=")]
    [Arguments("/about?rid=   ")]
    [Arguments("/guidelines?foo=bar")]
    [Arguments("/about?someOtherParam=value")]
    public async Task WhenPagesAreAccessed_TheyReturnHtml(string relativeUrl)
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, relativeUrl);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        // Ensure the response has content (not blank)
        string content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).IsNotEmpty();

        // Verify it's actually HTML content, not just whitespace
        await Assert.That(content).Contains("<html", StringComparison.OrdinalIgnoreCase);
    }

    [Test]
    [Arguments("/about?rid=abc", "https://essentialcsharp.com/about", "Learn about Essential C#")]
    [Arguments("/home", "https://essentialcsharp.com/home", "Explore Essential C#")]
    [Arguments("/cookie-policy", "https://essentialcsharp.com/cookie-policy", "uses cookies")]
    [Arguments("/guidelines", "https://essentialcsharp.com/guidelines", "coding guidelines")]
    [Arguments("/announcements", "https://essentialcsharp.com/announcements", "announcements")]
    [Arguments("/TermsOfService", "https://essentialcsharp.com/TermsOfService", "terms of service")]
    [Arguments("/mcp-setup", "https://essentialcsharp.com/mcp-setup", "Model Context Protocol")]
    [Arguments("/introducing-c", "https://essentialcsharp.com/introducing-c", null)]
    [Arguments("/hello-world", "https://essentialcsharp.com/introducing-c", null)]
    public async Task PagesHaveOneCanonicalAndPageSpecificDescriptions(string path, string expectedCanonical, string? expectedDescription)
    {
        await using WebApplicationFactory<Program> factory = CreateFactoryWithBaseUrl("https://essentialcsharp.com");
        await AssertPageMetadata(factory, path, expectedCanonical, expectedDescription);
    }

    [Test]
    [Arguments("/about", "https://dev.essentialcsharp.com/about", "Learn about Essential C#")]
    [Arguments("/introducing-c", "https://dev.essentialcsharp.com/introducing-c", null)]
    [Arguments("/hello-world?rid=abc", "https://dev.essentialcsharp.com/introducing-c", null)]
    public async Task CanonicalUrlsUseConfiguredDeploymentOrigin(string path, string expectedCanonical, string? expectedDescription)
    {
        await using WebApplicationFactory<Program> factory = CreateFactoryWithBaseUrl("https://dev.essentialcsharp.com/");
        await AssertPageMetadata(factory, path, expectedCanonical, expectedDescription);
    }

    private WebApplicationFactory<Program> CreateFactoryWithBaseUrl(string baseUrl) =>
        Factory.Inner.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<SiteSettings>(settings => settings.BaseUrl = baseUrl)));

    private static async Task AssertPageMetadata(WebApplicationFactory<Program> factory, string path, string expectedCanonical, string? expectedDescription)
    {
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

        HtmlDocument document = new();
        document.LoadHtml(await response.Content.ReadAsStringAsync());
        HtmlNode head = document.DocumentNode.SelectSingleNode("//head") ?? throw new InvalidOperationException("Missing head element");
        HtmlNodeCollection canonicals = head.SelectNodes("link[@rel='canonical']") ?? throw new InvalidOperationException("Missing canonical link");
        await Assert.That(canonicals.Count).IsEqualTo(1);
        await Assert.That(canonicals[0].GetAttributeValue("href", "")).IsEqualTo(expectedCanonical);
        await Assert.That((head.SelectSingleNode("meta[@property='og:url']") ?? throw new InvalidOperationException("Missing OG URL")).GetAttributeValue("content", "")).IsEqualTo(expectedCanonical);

        string description = (head.SelectSingleNode("meta[@name='description']") ?? throw new InvalidOperationException("Missing description")).GetAttributeValue("content", "");
        if (expectedDescription is null)
        {
            await Assert.That(description).IsNotEmpty();
            await Assert.That(description).DoesNotContain("Accelerate your development knowledge");
        }
        else
        {
            await Assert.That(description).Contains(expectedDescription);
        }
        await Assert.That((head.SelectNodes("meta[@name='description']") ?? throw new InvalidOperationException("Missing description")).Count).IsEqualTo(1);
        await Assert.That((head.SelectSingleNode("meta[@property='og:description']") ?? throw new InvalidOperationException("Missing OG description")).GetAttributeValue("content", "")).IsEqualTo(description);
        await Assert.That((head.SelectSingleNode("meta[@name='twitter:description']") ?? throw new InvalidOperationException("Missing Twitter description")).GetAttributeValue("content", "")).IsEqualTo(description);
    }

    [Test]
    public async Task NotFoundPageKeepsDescriptionFallbackWithoutCanonical()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, "/non-existing-page1234");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        HtmlDocument document = new();
        document.LoadHtml(await response.Content.ReadAsStringAsync());
        HtmlNode head = document.DocumentNode.SelectSingleNode("//head") ?? throw new InvalidOperationException("Missing head element");
        await Assert.That(head.SelectNodes("link[@rel='canonical']")).IsNull();
        await Assert.That((head.SelectSingleNode("meta[@name='description']") ?? throw new InvalidOperationException("Missing description")).GetAttributeValue("content", ""))
            .Contains("Accelerate your development knowledge");
    }

    [Test]
    public async Task WhenTheApplicationStarts_NonExistingPage_GivesCorrectStatusCode()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, "/non-existing-page1234");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

        string content = await response.Content.ReadAsStringAsync();
        await Assert.That(content).Contains("Go Home");
        await Assert.That(content).Contains("Go Back");
        await Assert.That(content).Contains("Browse Chapters");
    }
}
