using DotnetSitemapGenerator;
using DotnetSitemapGenerator.Serialization;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EssentialCSharp.Web.Helpers;

public static class SitemapXmlHelpers
{
    private const string RootUrl = "https://essentialcsharp.com/";

    public static void EnsureSitemapHealthy(List<SiteMapping> siteMappings)
    {
        var groups = siteMappings.GroupBy(item => new { item.ChapterNumber, item.PageNumber });
        foreach (var group in groups)
        {
            try
            {
                SiteMapping result = group.Single(item => item.IncludeInSitemapXml);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Sitemap error: Chapter {group.Key.ChapterNumber}, Page {group.Key.PageNumber} has more than one canonical link, or none: {ex.Message}", ex);
            }
        }
    }

    public static void GenerateAndSerializeSitemapXml(DirectoryInfo wwwrootDirectory, List<SiteMapping> siteMappings, ILogger logger, IActionDescriptorCollectionProvider actionDescriptorCollectionProvider)
    {
        GenerateSitemapXml(wwwrootDirectory, siteMappings, actionDescriptorCollectionProvider, out string xmlPath, out List<SitemapNode> nodes);
        XmlSerializer sitemapProvider = new();
        sitemapProvider.Serialize(new SitemapModel(nodes), xmlPath, true);
        logger.LogInformation("sitemap.xml successfully written to {XmlPath}", xmlPath);
    }

    public static void GenerateSitemapXml(DirectoryInfo wwwrootDirectory, List<SiteMapping> siteMappings, IActionDescriptorCollectionProvider actionDescriptorCollectionProvider, out string xmlPath, out List<SitemapNode> nodes)
    {
        xmlPath = Path.Combine(wwwrootDirectory.FullName, "sitemap.xml");
        DateTime newDateTime = DateTime.UtcNow;

        // Start with the root URL
        nodes = new() {
            new($"{RootUrl}")
            {
                LastModificationDate = newDateTime,
                ChangeFrequency = ChangeFrequency.Daily,
                Priority = 1.0M
            }
        };

        // Add routes dynamically discovered from controllers (excluding Identity routes)
        var controllerRoutes = GetControllerRoutes(actionDescriptorCollectionProvider);
        foreach (var route in controllerRoutes)
        {
            nodes.Add(new($"{RootUrl.TrimEnd('/')}{route}")
            {
                LastModificationDate = newDateTime,
                ChangeFrequency = GetChangeFrequencyForRoute(route),
                Priority = GetPriorityForRoute(route)
            });
        }

        // Add site mappings from content
        nodes.AddRange(siteMappings.Where(item => item.IncludeInSitemapXml).Select<SiteMapping, SitemapNode>(siteMapping => new($"{RootUrl}{siteMapping.Keys.First()}")
        {
            LastModificationDate = newDateTime,
            ChangeFrequency = ChangeFrequency.Daily,
            Priority = 0.8M
        }));
    }

    private static List<string> GetControllerRoutes(IActionDescriptorCollectionProvider actionDescriptorCollectionProvider)
    {
        var routes = new List<string>();

        foreach (var actionDescriptor in actionDescriptorCollectionProvider.ActionDescriptors.Items)
        {
            // Skip Identity area routes
            if (actionDescriptor.RouteValues.TryGetValue("area", out var area) && area == "Identity")
                continue;

            // Skip the default fallback route (Index action in HomeController)
            if (actionDescriptor.RouteValues.TryGetValue("action", out var action) && action == "Index")
                continue;

            // Skip Error actions
            if (action == "Error")
                continue;

            // Get the route template or attribute route
            if (actionDescriptor.AttributeRouteInfo?.Template is string template)
            {
                // Clean up the template (remove parameters, etc.)
                var cleanRoute = template.TrimStart('/');
                if (!string.IsNullOrEmpty(cleanRoute) && !routes.Contains($"/{cleanRoute}"))
                {
                    routes.Add($"/{cleanRoute}");
                }
            }
        }

        return routes.Distinct().OrderBy(r => r).ToList();
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

public class InvalidItemException : Exception
{
    public InvalidItemException(string? message) : base(message)
    {
    }
    public InvalidItemException(string? message, Exception exception) : base(message, exception)
    {
    }
}
