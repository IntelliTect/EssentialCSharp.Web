using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EssentialCSharp.Web.Controllers;

[AllowAnonymous]
public class McpSetupController : BaseController
{
    public McpSetupController(IRouteConfigurationService routeConfigurationService, IHttpContextAccessor httpContextAccessor)
        : base(routeConfigurationService, httpContextAccessor)
    {
    }

    [Route("/mcp-setup")]
    public IActionResult Index()
    {
        ViewBag.PageTitle = "MCP Setup";
        return View();
    }
}
