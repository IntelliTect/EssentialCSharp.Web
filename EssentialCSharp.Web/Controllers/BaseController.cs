using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EssentialCSharp.Web.Controllers;

public abstract class BaseController : Controller
{
    private readonly IRouteConfigurationService _RouteConfigurationService;

    protected BaseController(IRouteConfigurationService routeConfigurationService)
    {
        _RouteConfigurationService = routeConfigurationService;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        // Automatically add static routes to all views
        ViewBag.StaticRoutes = System.Text.Json.JsonSerializer.Serialize(_RouteConfigurationService.GetStaticRoutes());
        base.OnActionExecuting(context);
    }
}
