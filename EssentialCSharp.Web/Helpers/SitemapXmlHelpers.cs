using DotnetSitemapGenerator;
using DotnetSitemapGenerator.Serialization;
using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Helpers;

public static class SitemapXmlHelpers
{
    public static void EnsureSitemapHealthy(List<SiteMapping> siteMappings)
    {
        var groups = siteMappings.GroupBy(item => new { item.ChapterNumber, item.PageNumber });
        foreach (var group in groups)
        {
            var count = group.Count(item => item.IncludeInSitemapXml);
            if (count != 1)
            {
                throw new InvalidOperationException($"Sitemap error: Chapter {group.Key.ChapterNumber}, Page {group.Key.PageNumber} has more than one canonical link, or none");
            }
        }
    }

    public static void GenerateAndSerializeSitemapXml(DirectoryInfo wwwrootDirectory, List<SiteMapping> siteMappings, ILogger logger, IRouteConfigurationService routeConfigurationService, string baseUrl)
    {
        GenerateSitemapXml(wwwrootDirectory, siteMappings, routeConfigurationService, baseUrl, out List<SitemapNode> nodes);
        XmlSerializer sitemapProvider = new();
        var xmlPath = Path.Join(wwwrootDirectory.FullName, "sitemap.xml");
        sitemapProvider.Serialize(new SitemapModel(nodes), xmlPath, true);
        logger.LogInformation("sitemap.xml successfully written to {XmlPath}", xmlPath);
    }

    public static void GenerateSitemapXml(DirectoryInfo wwwrootDirectory, List<SiteMapping> siteMappings, IRouteConfigurationService routeConfigurationService, string baseUrl, out List<SitemapNode> nodes)
    {
        DateTime newDateTime = DateTime.UtcNow;

        // Routes should end up with leading slash
        baseUrl = baseUrl.TrimEnd('/');

        // Start with the root URL
        nodes = new() {
            new($"{baseUrl}/")
            {
                LastModificationDate = newDateTime,
                ChangeFrequency = ChangeFrequency.Daily,
                Priority = 1.0M
            }
        };

        // Add routes dynamically discovered from controllers
        var allRoutes = routeConfigurationService.GetStaticRoutes();
        var controllerRoutes = allRoutes
            .Where(route => !route.Contains("error", StringComparison.OrdinalIgnoreCase)) // Skip Error actions for sitemap
            .Where(route => !route.Contains("index", StringComparison.OrdinalIgnoreCase)) // Skip Index actions for sitemap
            .Where(route => !route.Contains("identity", StringComparison.OrdinalIgnoreCase)) // Skip Identity actions for sitemap
        // All routes should have leading slash
            .Select(route => $"/{route}") // Add leading slash for sitemap URLs
            .ToList();

        foreach (var route in controllerRoutes)
        {
            nodes.Add(new($"{baseUrl}{route}")
            {
                LastModificationDate = newDateTime,
                ChangeFrequency = GetChangeFrequencyForRoute(route),
                Priority = GetPriorityForRoute(route)
            });
        }

        // Add site mappings from content
        nodes.AddRange(siteMappings.Where(item => item.IncludeInSitemapXml).Select<SiteMapping, SitemapNode>(siteMapping => new($"{baseUrl.TrimEnd('/')}/{siteMapping.Keys.First()}")
        {
            LastModificationDate = siteMapping.LastModified ?? newDateTime,
            ChangeFrequency = ChangeFrequency.Daily,
            Priority = 0.8M
        }));
    }

    private static ChangeFrequency GetChangeFrequencyForRoute(string route)
    {
        return route.ToLowerInvariant() switch
        {
            "/termsofservice" => ChangeFrequency.Yearly,
            "/announcements" => ChangeFrequency.Monthly,
            "/guidelines" => ChangeFrequency.Monthly,
            _ => ChangeFrequency.Monthly
        };
    }

    private static decimal GetPriorityForRoute(string route)
    {
        return route.ToLowerInvariant() switch
        {
            "/home" => 0.5M,
            "/about" => 0.5M,
            "/announcements" => 0.5M,
            "/guidelines" => 0.9M,
            "/termsofservice" => 0.2M,
            _ => 0.5M
        };
    }
}
