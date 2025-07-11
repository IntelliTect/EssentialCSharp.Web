using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using Xunit;

namespace EssentialCSharp.Web.Tests.Controllers;

public class SitemapControllerTests : IClassFixture<WebApplicationFactory>
{
    private readonly WebApplicationFactory _factory;
    private readonly HttpClient _client;

    public SitemapControllerTests(WebApplicationFactory factory)
    {
        _factory = factory;
        _client = _factory.CreateClient();
    }

    [Fact]
    public async Task Sitemap_ReturnsXmlContent()
    {
        // Act
        var response = await _client.GetAsync("/sitemap.xml");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/xml; charset=utf-8", response.Content.Headers.ContentType?.ToString());
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", content);
        Assert.Contains("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">", content);
        Assert.Contains("</urlset>", content);
        
        // Verify it contains home page and some chapter URLs
        Assert.Contains("<loc>http://localhost/</loc>", content);
        Assert.Contains("introducing-c", content);
    }

    [Fact]
    public async Task IndexNowKeyFile_ReturnsKeyContent()
    {
        // Act - Use the placeholder key from appsettings.json
        var response = await _client.GetAsync("/placeholder-indexnow-key.txt");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.StartsWith("text/plain", response.Content.Headers.ContentType?.ToString());
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("placeholder-indexnow-key", content);
    }

    [Fact]
    public async Task IndexNowKeyFile_WithWrongKey_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync("/wrong-key.txt");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task NotifyIndexNow_ReturnsSuccess()
    {
        // Act
        var response = await _client.PostAsync("/api/notify-indexnow", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("IndexNow notifications sent successfully", content);
    }
}