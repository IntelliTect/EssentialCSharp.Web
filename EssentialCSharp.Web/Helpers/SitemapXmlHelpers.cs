using DotnetSitemapGenerator;
using EssentialCSharp.Web.Constants;
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

    public static void GenerateSitemapXml(IEnumerable<SiteMapping> siteMappings, IRouteConfigurationService routeConfigurationService, string baseUrl, out List<SitemapNode> nodes)
    {
        // Routes should end up with leading slash
        baseUrl = baseUrl.TrimEnd('/');

        // Start with the root URL — no LastModificationDate: it doesn't change per-request
        nodes = new() {
            new($"{baseUrl}/")
            {
                ChangeFrequency = ChangeFrequency.Daily,
                Priority = 1.0M
            }
        };

        // Add routes dynamically discovered from controllers (only indexable routes)
        var allRoutes = routeConfigurationService.GetIndexableRoutes();
        var controllerRoutes = allRoutes
            .Where(route => !IsSitemapRoute(route))
            .Select(route => $"/{route}")
            .ToList();

        foreach (var route in controllerRoutes)
        {
            nodes.Add(new($"{baseUrl}{route}")
            {
                ChangeFrequency = GetChangeFrequencyForRoute(route),
                Priority = GetPriorityForRoute(route)
            });
        }

        // Add site mappings from content
        nodes.AddRange(siteMappings.Where(item => item.IncludeInSitemapXml).Select(siteMapping =>
        {
            SitemapNode node = new($"{baseUrl.TrimEnd('/')}/{siteMapping.Keys.First()}")
            {
                ChangeFrequency = ChangeFrequency.Daily,
                Priority = 0.8M
            };

            if (siteMapping.LastModified is DateTime lastModified)
            {
                node.LastModificationDate = lastModified;
            }

            return node;
        }));
    }

    private static bool IsSitemapRoute(string route) =>
        route.TrimStart('/').Equals("sitemap.xml", StringComparison.OrdinalIgnoreCase);

    private static ChangeFrequency GetChangeFrequencyForRoute(string route)
    {
        if (RouteConstants.SeoMetadata.RouteConfig.TryGetValue(route, out var config))
        {
            return config.Frequency;
        }

        return ChangeFrequency.Monthly;
    }

    private static decimal GetPriorityForRoute(string route)
    {
        if (RouteConstants.SeoMetadata.RouteConfig.TryGetValue(route, out var config))
        {
            return config.Priority;
        }

        return 0.5M;
    }

}
