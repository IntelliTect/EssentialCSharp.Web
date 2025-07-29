using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace EssentialCSharp.Web.Controllers;

public abstract class BaseController : Controller
{
    private readonly IRouteConfigurationService _RouteConfigurationService;
    private readonly IHttpContextAccessor _HttpContextAccessor;

    protected BaseController(IRouteConfigurationService routeConfigurationService, IHttpContextAccessor httpContextAccessor)
    {
        _RouteConfigurationService = routeConfigurationService;
        _HttpContextAccessor = httpContextAccessor;
    }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        // Automatically add static routes to all views
        ViewBag.StaticRoutes = System.Text.Json.JsonSerializer.Serialize(_RouteConfigurationService.GetStaticRoutes());
        
        // Set the referral Id for use in the front end if available
        ViewBag.ReferralId = _HttpContextAccessor.HttpContext.GetReferrerId();
        
        base.OnActionExecuting(context);
    }
}
