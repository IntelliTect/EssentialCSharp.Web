using System.IO;
using System.Globalization;
using DotnetSitemapGenerator;
using EssentialCSharp.Web.Helpers;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace EssentialCSharp.Web.Tests;

[NotInParallel]
[ClassDataSource<WebApplicationFactory>(Shared = SharedType.PerClass)]
public class SitemapXmlHelpersTests
{
    private readonly WebApplicationFactory _Factory;

    public SitemapXmlHelpersTests(WebApplicationFactory factory)
    {
        _Factory = factory;
    }

    [Test]
    public async Task EnsureSitemapHealthy_WithValidSiteMappings_DoesNotThrow()
    {
        // Arrange
        var siteMappings = new List<SiteMapping>
        {
            CreateSiteMapping(1, 1, true),
            CreateSiteMapping(1, 2, true),
            CreateSiteMapping(2, 1, true)
        };

        // Act & Assert
        await Assert.That(() => SitemapXmlHelpers.EnsureSitemapHealthy(siteMappings)).ThrowsNothing();
    }

    [Test]
    public async Task EnsureSitemapHealthy_WithMultipleCanonicalLinksForSamePage_ThrowsException()
    {
        // Arrange - Two mappings for the same chapter/page both marked as canonical
        var siteMappings = new List<SiteMapping>
        {
            CreateSiteMapping(1, 1, true),
            CreateSiteMapping(1, 1, true) // Same chapter/page, also canonical
        };

        // Act & Assert
        await Assert.That(() => SitemapXmlHelpers.EnsureSitemapHealthy(siteMappings))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Chapter 1, Page 1")
            .And.HasMessageContaining("more than one canonical link");
    }

    [Test]
    public async Task EnsureSitemapHealthy_WithNoCanonicalLinksForPage_ThrowsException()
    {
        // Arrange - No mappings marked as canonical for this page
        var siteMappings = new List<SiteMapping>
        {
            CreateSiteMapping(1, 1, false),
            CreateSiteMapping(1, 1, false) // Same chapter/page, neither canonical
        };

        // Act & Assert
        await Assert.That(() => SitemapXmlHelpers.EnsureSitemapHealthy(siteMappings))
            .Throws<InvalidOperationException>()
            .WithMessageContaining("Chapter 1, Page 1");
    }

    [Test]
    public async Task GenerateSitemapXml_DoesNotIncludeIdentityRoutes()
    {
        // Arrange
        var tempDir = new DirectoryInfo(Path.GetTempPath());
        var siteMappings = new List<SiteMapping> { CreateSiteMapping(1, 1, true) };
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var routeConfigurationService = _Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            tempDir,
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        var allUrls = nodes.Select(n => n.Url).ToList();

        // Verify no Identity routes are included
        await Assert.That(allUrls).DoesNotContain(url => url.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        await Assert.That(allUrls).DoesNotContain(url => url.Contains("Account", StringComparison.OrdinalIgnoreCase));

        // But verify that expected routes are included
        await Assert.That(allUrls).Contains(url => url.Contains("/home", StringComparison.OrdinalIgnoreCase));
        await Assert.That(allUrls).Contains(url => url.Contains("/about", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task GenerateSitemapXml_IncludesBaseUrl()
    {
        // Arrange
        var tempDir = new DirectoryInfo(Path.GetTempPath());
        var siteMappings = new List<SiteMapping>();
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var routeConfigurationService = _Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            tempDir,
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        await Assert.That(nodes).Contains(node => node.Url == baseUrl);

        // Verify the root URL has highest priority
        var rootNode = nodes.First(node => node.Url == baseUrl);
        await Assert.That(rootNode.Priority).IsEqualTo(1.0M);
        await Assert.That(rootNode.ChangeFrequency).IsEqualTo(ChangeFrequency.Daily);
    }

    [Test]
    public async Task GenerateSitemapXml_IncludesSiteMappingsMarkedForXml()
    {
        // Arrange
        var tempDir = new DirectoryInfo(Path.GetTempPath());
        var baseUrl = "https://test.example.com/";

        var siteMappings = new List<SiteMapping>
        {
            CreateSiteMapping(1, 1, true, "test-page-1"),
            CreateSiteMapping(1, 2, false, "test-page-2"), // Not included in XML
            CreateSiteMapping(2, 1, true, "test-page-3")
        };

        // Act & Assert
        var routeConfigurationService = _Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            tempDir,
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        var allUrls = nodes.Select(n => n.Url).ToList();

        await Assert.That(allUrls).Contains(url => url.Contains("test-page-1"));
        await Assert.That(allUrls).DoesNotContain(url => url.Contains("test-page-2")); // Not marked for XML
        await Assert.That(allUrls).Contains(url => url.Contains("test-page-3"));
    }

    [Test]
    public async Task GenerateSitemapXml_DoesNotIncludeIndexRoutes()
    {
        // Arrange
        var tempDir = new DirectoryInfo(Path.GetTempPath());
        var siteMappings = new List<SiteMapping>();
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var routeConfigurationService = _Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            tempDir,
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        var allUrls = nodes.Select(n => n.Url).ToList();

        // Should not include Index action routes (they're the default)
        await Assert.That(allUrls).DoesNotContain(url => url.Contains("/Index", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task GenerateSitemapXml_DoesNotIncludeErrorRoutes()
    {
        // Arrange
        var tempDir = new DirectoryInfo(Path.GetTempPath());
        var siteMappings = new List<SiteMapping>();
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var routeConfigurationService = _Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            tempDir,
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        var allUrls = nodes.Select(n => n.Url).ToList();

        // Should not include Error action routes
        await Assert.That(allUrls).DoesNotContain(url => url.Contains("/Error", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task GenerateSitemapXml_UsesLastModifiedDateFromSiteMapping()
    {
        // Arrange
        var tempDir = new DirectoryInfo(Path.GetTempPath());
        var baseUrl = "https://test.example.com/";
        var specificLastModified = new DateTime(2023, 5, 15, 10, 30, 0, DateTimeKind.Utc);

        var siteMappings = new List<SiteMapping>
        {
            CreateSiteMapping(1, 1, true, "test-page-1", specificLastModified)
        };

        // Act
        var routeConfigurationService = _Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            tempDir,
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        // Assert
        var siteMappingNode = nodes.First(node => node.Url.Contains("test-page-1"));
        await Assert.That(siteMappingNode.LastModificationDate).IsEqualTo(specificLastModified);
    }

    private static SiteMapping CreateSiteMapping(
        int chapterNumber,
        int pageNumber,
        bool includeInSitemapXml,
        string key = "test-key",
        DateTime? lastModified = null)
    {
        return new SiteMapping(
            keys: [key],
            primaryKey: key,
            pagePath: ["Chapters", chapterNumber.ToString("00", CultureInfo.InvariantCulture), "Pages", $"{pageNumber:00}.html"],
            chapterNumber: chapterNumber,
            pageNumber: pageNumber,
            orderOnPage: 0,
            chapterTitle: $"Chapter {chapterNumber}",
            rawHeading: "Test Heading",
            anchorId: key,
            indentLevel: 1,
            contentHash: "TestHash123",
            includeInSitemapXml: includeInSitemapXml,
            lastModified: lastModified
        );
    }
}