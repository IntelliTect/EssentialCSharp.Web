using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Extensions;
using HtmlAgilityPack;
using System.Text.Json;

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
        key ??= _SiteMappings.First().Heading;
        key = key.SanitizeKey();
        SiteMapping? siteMapping = _SiteMappings.FirstOrDefault(x => x.Heading == key);
        if (siteMapping != null)
        {
            string filePath = Path.Combine(_HostingEnvironment.ContentRootPath, siteMapping.PagePath);
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
