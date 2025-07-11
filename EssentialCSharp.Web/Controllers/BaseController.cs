using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Controllers;

public abstract class BaseController : Controller
{
    private readonly IRouteConfigurationService _routeConfigurationService;

    protected BaseController(IRouteConfigurationService routeConfigurationService)
    {
        _routeConfigurationService = routeConfigurationService;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        // Automatically add static routes to all views
        ViewBag.StaticRoutes = System.Text.Json.JsonSerializer.Serialize(_routeConfigurationService.GetStaticRoutes());
        base.OnActionExecuting(context);
    }
}
