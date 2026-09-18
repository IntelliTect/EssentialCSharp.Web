using DotnetSitemapGenerator;
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

    public static void GenerateSitemapXml(
        IEnumerable<SiteMapping> siteMappings,
        IRouteConfigurationService routeConfigurationService,
        string baseUrl,
        out List<SitemapNode> nodes) =>
        GenerateSitemapXml(siteMappings, routeConfigurationService, baseUrl, staticRouteLastModifiedDates: null, out nodes);

    public static void GenerateSitemapXml(
        IEnumerable<SiteMapping> siteMappings,
        IRouteConfigurationService routeConfigurationService,
        string baseUrl,
        IReadOnlyDictionary<string, DateTime>? staticRouteLastModifiedDates,
        out List<SitemapNode> nodes)
    {
        // Routes should end up with leading slash
        baseUrl = baseUrl.TrimEnd('/');

        // Start with the root URL — no LastModificationDate: it doesn't change per-request
        var rootNode = new SitemapNode($"{baseUrl}/")
        {
            ChangeFrequency = ChangeFrequency.Daily,
            Priority = 1.0M
        };

        if (TryGetRouteLastModified(staticRouteLastModifiedDates, "/home") is DateTime homeLastModified)
        {
            rootNode.LastModificationDate = homeLastModified;
        }

        nodes = new() {
            rootNode
        };

        // Add routes dynamically discovered from controllers (only indexable routes)
        var allRoutes = routeConfigurationService.GetIndexableRoutes();
        var controllerRoutes = allRoutes
            .Where(route => !IsSitemapRoute(route))
            .Select(route => $"/{route}")
            .ToList();

        foreach (var route in controllerRoutes)
        {
            var node = new SitemapNode($"{baseUrl}{route}")
            {
                ChangeFrequency = GetChangeFrequencyForRoute(route),
                Priority = GetPriorityForRoute(route)
            };

            if (TryGetRouteLastModified(staticRouteLastModifiedDates, route) is DateTime routeLastModified)
            {
                node.LastModificationDate = routeLastModified;
            }

            nodes.Add(node);
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

    private static DateTime? TryGetRouteLastModified(IReadOnlyDictionary<string, DateTime>? staticRouteLastModifiedDates, string route)
    {
        if (staticRouteLastModifiedDates is null)
        {
            return null;
        }

        var normalizedRoute = NormalizeRoute(route);
        return staticRouteLastModifiedDates.TryGetValue(normalizedRoute, out var lastModified) ? lastModified : null;
    }

    private static string NormalizeRoute(string route)
    {
        route = route.Trim();
        if (route == "/")
        {
            return route;
        }

        return $"/{route.TrimStart('/').ToLowerInvariant()}";
    }

    private static bool IsSitemapRoute(string route) =>
        route.TrimStart('/').Equals("sitemap.xml", StringComparison.OrdinalIgnoreCase);

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
