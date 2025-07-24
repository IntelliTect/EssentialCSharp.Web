using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace EssentialCSharp.Web.Services;

public class RouteConfigurationService : IRouteConfigurationService
{
    private readonly IActionDescriptorCollectionProvider _ActionDescriptorCollectionProvider;
    private readonly HashSet<string> _StaticRoutes;

    public RouteConfigurationService(IActionDescriptorCollectionProvider actionDescriptorCollectionProvider)
    {
        _ActionDescriptorCollectionProvider = actionDescriptorCollectionProvider;
        _StaticRoutes = ExtractStaticRoutes();
    }

    public IReadOnlySet<string> GetStaticRoutes()
    {
        return _StaticRoutes;
    }

    private HashSet<string> ExtractStaticRoutes()
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Get all action descriptors
        var actionDescriptors = _ActionDescriptorCollectionProvider.ActionDescriptors.Items;
        
        foreach (var actionDescriptor in actionDescriptors)
        {
            // Look for route attributes
            if (actionDescriptor.AttributeRouteInfo?.Template != null)
            {
                string template = actionDescriptor.AttributeRouteInfo.Template;
                
                // Remove leading slash and add to our set
                string routePath = template.TrimStart('/').ToLowerInvariant();
                routes.Add(routePath);
            }

            // Skip the default fallback route (Index action in HomeController)
            if (actionDescriptor.RouteValues.TryGetValue("action", out var action) && action == "Index")
                continue;

            // Skip Error actions
            if (action == "Error")
                continue;

            // For actions without attribute routes, use conventional routing
            if (actionDescriptor.AttributeRouteInfo?.Template == null &&
                actionDescriptor.RouteValues.TryGetValue("action", out var actionName) && 
                actionDescriptor.RouteValues.TryGetValue("controller", out var controllerName))
            {
                if (controllerName?.Equals("Home", StringComparison.OrdinalIgnoreCase) == true && actionName != null)
                {
                    // Use the action name directly as the route
                    routes.Add(actionName.ToLowerInvariant());
                }
            }
        }

        return routes;
    }
}
