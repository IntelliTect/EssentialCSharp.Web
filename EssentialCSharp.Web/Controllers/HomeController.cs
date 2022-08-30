using EssentialCSharp.Web.Models;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;

namespace EssentialCSharp.Web.Controllers;

public class HomeController : Controller
{
    private readonly IWebHostEnvironment _HostingEnvironment;
    private readonly IList<SiteMapping> _SiteMappings;
    private readonly ILogger<HomeController> _Logger;

    public HomeController(ILogger<HomeController> logger, IWebHostEnvironment hostingEnvironment, IList<SiteMapping> siteMappings)
    {
        _Logger = logger;
        _HostingEnvironment = hostingEnvironment;
        _SiteMappings = siteMappings;
    }

    public IActionResult Index(string key)
    {
        string? html = null;

        // if no key (default case), then load up first page
        SiteMapping? siteMapping = SiteMapping.Find(key, _SiteMappings);
        if (siteMapping != null)
        {
            string filePath = Path.Combine(_HostingEnvironment.ContentRootPath, Path.Combine(siteMapping.PagePath));
            HtmlDocument doc = new();
            doc.Load(filePath);
            html = doc.DocumentNode.InnerHtml;
        }
        else
        {
            return RedirectToAction(nameof(Error), new { errorMessage = "Specified page not found, please check your spelling and try again" });
        }
        if (html is null)
        {
            return RedirectToAction(nameof(Error), new { errorMessage = "Unexpected Exception; html being rendered is null" });
        }
        ViewBag.Contents = html;
        return View();
    }

    public IActionResult TermsOfService()
    {

        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(string? errorMessage = null)
    {
        ViewBag.ErrorMessage = errorMessage;
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
