using System.Globalization;
using DotnetSitemapGenerator;
using EssentialCSharp.Web.Helpers;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;
namespace EssentialCSharp.Web.Tests;

public class SitemapXmlHelpersTests : IntegrationTestBase
{
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
    public async Task GenerateSitemapXml_DoesNotIncludeApiRoutes()
    {
        // Arrange
        var siteMappings = new List<SiteMapping> { CreateSiteMapping(1, 1, true) };
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        var allUrls = nodes.Select(n => n.Url).ToList();

        // Verify no API routes are included (assert on the /api/ pattern, not specific controller names)
        await Assert.That(allUrls).DoesNotContain(url => url.Contains("/api/", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task GenerateSitemapXml_DoesNotIncludeParameterizedRoutes()
    {
        // Arrange
        var siteMappings = new List<SiteMapping> { CreateSiteMapping(1, 1, true) };
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        var allUrls = nodes.Select(n => n.Url).ToList();

        // Verify no parameterized routes (with {}) are included
        await Assert.That(allUrls).DoesNotContain(url => url.Contains('{'));
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
            .And.WithMessageContaining("more than one canonical link");
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
        var siteMappings = new List<SiteMapping> { CreateSiteMapping(1, 1, true) };
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
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
        var siteMappings = new List<SiteMapping>();
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        await Assert.That(nodes).Contains(node => node.Url == baseUrl);

        // Verify the root URL has highest priority
        var rootNode = nodes.First(node => node.Url == baseUrl);
        using (Assert.Multiple())
        {
            await Assert.That(rootNode.Priority).IsEqualTo(1.0M);
            await Assert.That(rootNode.ChangeFrequency).IsEqualTo(ChangeFrequency.Daily);
        }
    }

    [Test]
    public async Task GenerateSitemapXml_IncludesSiteMappingsMarkedForXml()
    {
        // Arrange
        var baseUrl = "https://test.example.com/";

        var siteMappings = new List<SiteMapping>
        {
            CreateSiteMapping(1, 1, true, "test-page-1"),
            CreateSiteMapping(1, 2, false, "test-page-2"), // Not included in XML
            CreateSiteMapping(2, 1, true, "test-page-3")
        };

        // Act & Assert
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        var allUrls = nodes.Select(n => n.Url).ToList();

        await Assert.That(allUrls).Contains(url => url.Contains("test-page-1", StringComparison.OrdinalIgnoreCase));
        await Assert.That(allUrls).DoesNotContain(url => url.Contains("test-page-2", StringComparison.OrdinalIgnoreCase)); // Not marked for XML
        await Assert.That(allUrls).Contains(url => url.Contains("test-page-3", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task GenerateSitemapXml_DoesNotIncludeIndexRoutes()
    {
        // Arrange
        var siteMappings = new List<SiteMapping>();
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
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
        var siteMappings = new List<SiteMapping>();
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        var allUrls = nodes.Select(n => n.Url).ToList();

        // Should not include Error action routes
        await Assert.That(allUrls).DoesNotContain(url => url.Contains("/Error", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task GenerateSitemapXml_DoesNotIncludeSitemapRoute()
    {
        // Arrange
        var siteMappings = new List<SiteMapping>();
        var baseUrl = "https://test.example.com/";

        // Act
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        var allUrls = nodes.Select(n => n.Url).ToList();

        // /sitemap.xml should not list itself
        await Assert.That(allUrls).DoesNotContain(url => url.EndsWith("/sitemap.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Test]
    public async Task GenerateSitemapXml_UsesLastModifiedDateFromSiteMapping()
    {
        // Arrange
        var baseUrl = "https://test.example.com/";
        var specificLastModified = new DateTime(2023, 5, 15, 10, 30, 0, DateTimeKind.Utc);

        var siteMappings = new List<SiteMapping>
        {
            CreateSiteMapping(1, 1, true, "test-page-1", specificLastModified)
        };

        // Act
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        // Assert
        var siteMappingNode = nodes.First(node => node.Url.Contains("test-page-1"));
        await Assert.That(siteMappingNode.LastModificationDate).IsEqualTo(specificLastModified);
    }

    [Test]
    public async Task GenerateSitemapXml_DoesNotSetLastModifiedDateWhenSiteMappingDateIsMissing()
    {
        // Arrange
        var baseUrl = "https://test.example.com/";
        var siteMappings = new List<SiteMapping>
        {
            CreateSiteMapping(1, 1, true, "test-page-1")
        };

        // Act
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappings,
            routeConfigurationService,
            baseUrl,
            out var nodes);

        // Assert
        var siteMappingNode = nodes.First(node => node.Url.Contains("test-page-1"));
        await Assert.That(siteMappingNode.LastModificationDate).IsNull();
    }

    [Test]
    public async Task GenerateSitemapXml_UsesLastModifiedDateFromStaticRouteLookup()
    {
        // Arrange
        var baseUrl = "https://test.example.com/";
        var homeLastModified = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var staticRouteLastModifiedDates = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
        {
            ["/home"] = homeLastModified
        };
        var siteMappings = new List<SiteMapping>();

        // Act
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappings,
            routeConfigurationService,
            baseUrl,
            staticRouteLastModifiedDates,
            out var nodes);

        // Assert
        var homeNode = nodes.First(node => node.Url.EndsWith("/home", StringComparison.OrdinalIgnoreCase));
        await Assert.That(homeNode.LastModificationDate).IsEqualTo(homeLastModified);
    }

    [Test]
    public async Task GenerateSitemapXml_AppliesLastModifiedDateForAllMappedStaticRoutes()
    {
        // Arrange
        var baseUrl = "https://test.example.com/";
        var staticRouteLastModifiedDates = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
        {
            ["/home"] = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            ["/about"] = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc),
            ["/guidelines"] = new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc)
        };
        var siteMappings = new List<SiteMapping>();

        // Act
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappings,
            routeConfigurationService,
            baseUrl,
            staticRouteLastModifiedDates,
            out var nodes);

        // Assert
        foreach ((var route, var expectedLastModified) in staticRouteLastModifiedDates)
        {
            var routeNode = nodes.First(node => node.Url.EndsWith(route, StringComparison.OrdinalIgnoreCase));
            await Assert.That(routeNode.LastModificationDate).IsEqualTo(expectedLastModified);
        }
    }

    [Test]
    public async Task GenerateSitemapXml_UsesHomeLastModifiedDateForRootNode()
    {
        // Arrange
        var baseUrl = "https://test.example.com/";
        var homeLastModified = new DateTime(2025, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        var staticRouteLastModifiedDates = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase)
        {
            ["/home"] = homeLastModified
        };
        var siteMappings = new List<SiteMapping>();

        // Act
        var routeConfigurationService = Factory.Services.GetRequiredService<IRouteConfigurationService>();
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappings,
            routeConfigurationService,
            baseUrl,
            staticRouteLastModifiedDates,
            out var nodes);

        // Assert
        var rootNode = nodes.First(node => node.Url == baseUrl);
        await Assert.That(rootNode.LastModificationDate).IsEqualTo(homeLastModified);
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
