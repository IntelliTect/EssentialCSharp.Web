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

        // if no key (default case), then load up home page
        SiteMapping? siteMapping = SiteMapping.Find(key, _SiteMappings);

        if (string.IsNullOrEmpty(key))
        {
            return RedirectToAction(nameof(Home)); 
        }
        else if (siteMapping is not null)
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
        ViewBag.NextPage = FlipPage(siteMapping!.ChapterNumber, siteMapping.PageNumber, true);
        ViewBag.PreviousPage = FlipPage(siteMapping.ChapterNumber, siteMapping.PageNumber, false);
        ViewBag.Contents = html;
        return View();
    }

    [Route("/TermsOfService",
       Name = "TermsOfService")]
    public IActionResult TermsOfService()
    {
        return View();
    }

    [Route("/home",
        Name = "home")]
    public IActionResult Home()
    {
        return View();
    }

    private string FlipPage(int currentChapter, int currentPage, bool next)
    {
        if (_SiteMappings.Count == 0)
        {
            return "";
        }

        int page = -1;
        if (next)
        {
            page = 1;
        }

        SiteMapping? siteMap = _SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter && f.PageNumber == currentPage + page);

        if (siteMap is null)
        {
            if (next)
            {
                siteMap = _SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter + 1 && f.PageNumber == 1);
            }
            else
            {
                int? previousPage = _SiteMappings.LastOrDefault(f => f.ChapterNumber == currentChapter - 1)?.PageNumber;
                siteMap = _SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter - 1 && f.PageNumber == previousPage);
            }
            if (siteMap is null)
            {
                return "";
            }
        }
        return $"{siteMap.Key}#{siteMap.AnchorId}";
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(string? errorMessage = null)
    {
        ViewBag.ErrorMessage = errorMessage;
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
