using System.Globalization;
using DotnetSitemapGenerator;
using EssentialCSharp.Web.Helpers;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EssentialCSharp.Web.Tests;

public class SitemapXmlHelpersTests : IClassFixture<WebApplicationFactory>
{
    private readonly WebApplicationFactory _Factory;

    public SitemapXmlHelpersTests(WebApplicationFactory factory)
    {
        _Factory = factory;
    }

    [Fact]
    public void EnsureSitemapHealthy_WithValidSiteMappings_DoesNotThrow()
    {
        // Arrange
        var siteMappings = new List<SiteMapping>
        {
            CreateSiteMapping(1, 1, true),
            CreateSiteMapping(1, 2, true),
            CreateSiteMapping(2, 1, true)
        };

        // Act & Assert
        var exception = Record.Exception(() => SitemapXmlHelpers.EnsureSitemapHealthy(siteMappings));
        Assert.Null(exception);
    }

    [Fact]
    public void EnsureSitemapHealthy_WithMultipleCanonicalLinksForSamePage_ThrowsException()
    {
        // Arrange - Two mappings for the same chapter/page both marked as canonical
        var siteMappings = new List<SiteMapping>
        {
            CreateSiteMapping(1, 1, true),
            CreateSiteMapping(1, 1, true) // Same chapter/page, also canonical
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SitemapXmlHelpers.EnsureSitemapHealthy(siteMappings));

        Assert.Contains("Chapter 1, Page 1", exception.Message);
        Assert.Contains("more than one canonical link", exception.Message);
    }

    [Fact]
    public void EnsureSitemapHealthy_WithNoCanonicalLinksForPage_ThrowsException()
    {
        // Arrange - No mappings marked as canonical for this page
        var siteMappings = new List<SiteMapping>
        {
            CreateSiteMapping(1, 1, false),
            CreateSiteMapping(1, 1, false) // Same chapter/page, neither canonical
        };

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            SitemapXmlHelpers.EnsureSitemapHealthy(siteMappings));

        Assert.Contains("Chapter 1, Page 1", exception.Message);
    }

    [Fact]
    public void GenerateSitemapXml_DoesNotIncludeIdentityRoutes()
    {
        // Arrange
        var tempDir = new DirectoryInfo(Path.GetTempPath());
        var siteMappings = new List<SiteMapping> { CreateSiteMapping(1, 1, true) };
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var nodes = _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            SitemapXmlHelpers.GenerateSitemapXml(
                tempDir,
                siteMappings,
                routeConfigurationService,
                baseUrl,
                out var nodes);
            return nodes;
        });

        var allUrls = nodes.Select(n => n.Url).ToList();

        // Verify no Identity routes are included
        Assert.DoesNotContain(allUrls, url => url.Contains("Identity", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(allUrls, url => url.Contains("Account", StringComparison.OrdinalIgnoreCase));

        // But verify that expected routes are included
        Assert.Contains(allUrls, url => url.Contains("/home", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(allUrls, url => url.Contains("/about", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GenerateSitemapXml_IncludesBaseUrl()
    {
        // Arrange
        var tempDir = new DirectoryInfo(Path.GetTempPath());
        var siteMappings = new List<SiteMapping>();
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        var nodes = _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            SitemapXmlHelpers.GenerateSitemapXml(
                tempDir,
                siteMappings,
                routeConfigurationService,
                baseUrl,
                out var nodes);
            return nodes;
        });

        Assert.Contains(nodes, node => node.Url == baseUrl);

        // Verify the root URL has highest priority
        var rootNode = nodes.First(node => node.Url == baseUrl);
        Assert.Equal(1.0M, rootNode.Priority);
        Assert.Equal(ChangeFrequency.Daily, rootNode.ChangeFrequency);
    }

    [Fact]
    public void GenerateSitemapXml_IncludesSiteMappingsMarkedForXml()
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
        _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            SitemapXmlHelpers.GenerateSitemapXml(
                tempDir,
                siteMappings,
                routeConfigurationService,
                baseUrl,
                out var nodes);

            var allUrls = nodes.Select(n => n.Url).ToList();

            Assert.Contains(allUrls, url => url.Contains("test-page-1"));
            Assert.DoesNotContain(allUrls, url => url.Contains("test-page-2")); // Not marked for XML
            Assert.Contains(allUrls, url => url.Contains("test-page-3"));
        });
    }

    [Fact]
    public void GenerateSitemapXml_DoesNotIncludeIndexRoutes()
    {
        // Arrange
        var tempDir = new DirectoryInfo(Path.GetTempPath());
        var siteMappings = new List<SiteMapping>();
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            SitemapXmlHelpers.GenerateSitemapXml(
                tempDir,
                siteMappings,
                routeConfigurationService,
                baseUrl,
                out var nodes);

            var allUrls = nodes.Select(n => n.Url).ToList();

            // Should not include Index action routes (they're the default)
            Assert.DoesNotContain(allUrls, url => url.Contains("/Index", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void GenerateSitemapXml_DoesNotIncludeErrorRoutes()
    {
        // Arrange
        var tempDir = new DirectoryInfo(Path.GetTempPath());
        var siteMappings = new List<SiteMapping>();
        var baseUrl = "https://test.example.com/";

        // Act & Assert
        _Factory.InServiceScope(serviceProvider =>
        {
            var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
            SitemapXmlHelpers.GenerateSitemapXml(
                tempDir,
                siteMappings,
                routeConfigurationService,
                baseUrl,
                out var nodes);

            var allUrls = nodes.Select(n => n.Url).ToList();

            // Should not include Error action routes
            Assert.DoesNotContain(allUrls, url => url.Contains("/Error", StringComparison.OrdinalIgnoreCase));
        });
    }

    [Fact]
    public void GenerateAndSerializeSitemapXml_CreatesFileSuccessfully()
    {
        // Arrange
        var logger = _Factory.Services.GetRequiredService<ILogger<SitemapXmlHelpersTests>>();
        var tempDir = new DirectoryInfo(Path.GetTempPath());
        var siteMappings = new List<SiteMapping> { CreateSiteMapping(1, 1, true) };
        var baseUrl = "https://test.example.com/";

        // Clean up any existing file
        var expectedXmlPath = Path.Combine(tempDir.FullName, "sitemap.xml");
        if (File.Exists(expectedXmlPath))
        {
            File.Delete(expectedXmlPath);
        }

        try
        {
            // Act
            _Factory.InServiceScope(serviceProvider =>
            {
                var routeConfigurationService = serviceProvider.GetRequiredService<IRouteConfigurationService>();
                SitemapXmlHelpers.GenerateAndSerializeSitemapXml(
                    tempDir,
                    siteMappings,
                    logger,
                    routeConfigurationService,
                    baseUrl);
            });

            // Assert
            Assert.True(File.Exists(expectedXmlPath));

            var xmlContent = File.ReadAllText(expectedXmlPath);
            Assert.Contains("<?xml", xmlContent);
            Assert.Contains("<urlset", xmlContent);
            Assert.Contains(baseUrl, xmlContent);
        }
        finally
        {
            // Clean up
            if (File.Exists(expectedXmlPath))
            {
                File.Delete(expectedXmlPath);
            }
        }
    }

    private static SiteMapping CreateSiteMapping(
        int chapterNumber,
        int pageNumber,
        bool includeInSitemapXml,
        string key = "test-key")
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
            includeInSitemapXml: includeInSitemapXml
        );
    }
}
