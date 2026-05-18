using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using System.Text.RegularExpressions;

namespace EssentialCSharp.Web.Services;

public class RouteConfigurationService : IRouteConfigurationService
{
    private readonly IActionDescriptorCollectionProvider _ActionDescriptorCollectionProvider;
    private readonly HashSet<string> _StaticRoutes;
    private readonly HashSet<string> _IndexableRoutes;

    public RouteConfigurationService(IActionDescriptorCollectionProvider actionDescriptorCollectionProvider)
    {
        _ActionDescriptorCollectionProvider = actionDescriptorCollectionProvider;
        _StaticRoutes = ExtractStaticRoutes();
        _IndexableRoutes = ExtractIndexableRoutes();
    }

    public IReadOnlySet<string> GetStaticRoutes()
    {
        return _StaticRoutes;
    }

    public IReadOnlySet<string> GetIndexableRoutes()
    {
        return _IndexableRoutes;
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
                actionDescriptor.RouteValues.TryGetValue("controller", out var controllerName) &&
                controllerName?.Equals("Home", StringComparison.OrdinalIgnoreCase) == true &&
                actionName != null)
            {
                // Use the action name directly as the route
                routes.Add(actionName.ToLowerInvariant());
            }
        }

        return routes;
    }

    private HashSet<string> ExtractIndexableRoutes()
    {
        var indexableRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        // Get all action descriptors
        var actionDescriptors = _ActionDescriptorCollectionProvider.ActionDescriptors.Items;
        
        foreach (var actionDescriptor in actionDescriptors)
        {
            // Skip if controller is marked with [ApiController]
            if (IsApiController(actionDescriptor))
                continue;

            // Look for route attributes
            if (actionDescriptor.AttributeRouteInfo?.Template != null)
            {
                string template = actionDescriptor.AttributeRouteInfo.Template;
                
                // Skip routes with parameters (e.g., {chapter}, {id:guid}, [optional])
                if (ContainsRouteParameters(template))
                    continue;
                
                // Skip routes starting with /api/
                if (template.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                    continue;
                
                // Remove leading slash and add to our set
                string routePath = template.TrimStart('/').ToLowerInvariant();
                indexableRoutes.Add(routePath);
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
                actionDescriptor.RouteValues.TryGetValue("controller", out var controllerName) &&
                controllerName?.Equals("Home", StringComparison.OrdinalIgnoreCase) == true &&
                actionName != null)
            {
                // Use the action name directly as the route
                indexableRoutes.Add(actionName.ToLowerInvariant());
            }
        }

        return indexableRoutes;
    }

    private static bool IsApiController(ActionDescriptor actionDescriptor)
    {
        // Check for [ApiController] attribute
        if (actionDescriptor.EndpointMetadata?.OfType<ApiControllerAttribute>().Any() == true)
            return true;
        
        // Check if controller inherits from ControllerBase (not Controller)
        if (actionDescriptor.RouteValues.TryGetValue("controller", out var controllerName))
        {
            // Known API controllers
            var apiControllers = new[] { "ListingSourceCode", "Chat", "McpToken", "MCP" };
            if (apiControllers.Contains(controllerName, StringComparer.OrdinalIgnoreCase))
                return true;
        }
        
        return false;
    }

    private static bool ContainsRouteParameters(string template)
    {
        // Match {param}, {param:constraint}, [optional], etc.
        return Regex.IsMatch(template, @"\{[^}]+\}|\[[^\]]+\]");
    }
}

