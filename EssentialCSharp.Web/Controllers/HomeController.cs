using EssentialCSharp.Web.Models;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
        SiteMapping currentSiteMapping;

        // if no key (default case), then load up first page
        SiteMapping? siteMapping = SiteMapping.Find(key, _SiteMappings);
        if (siteMapping != null)
        {
            currentSiteMapping = siteMapping;
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
        ViewBag.NextPage = FlipPage(currentSiteMapping, true);
        ViewBag.PreviousPage = FlipPage(currentSiteMapping, false);
        ViewBag.Contents = html;
        return View();
    }

    [Route("/TermsOfService",
       Name = "TermsOfService")]
    public IActionResult TermsOfService()
    {
        return View();
    }

    private string FlipPage(SiteMapping currentSiteMapping, bool next)
    {
        if(_SiteMappings.Count == 0)
        {
            return "";
        }
        int currentChapter = currentSiteMapping.ChapterNumber;
        int currentPage    = currentSiteMapping.PageNumber;

        int page = -1;
        if (next)
        {
            page = 1;
        }

        SiteMapping? siteMap = _SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter && f.PageNumber == currentPage + page);
        
        if(siteMap == null)
        {
            if (next)
            {
                siteMap = _SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter + 1 && f.PageNumber == 1) ?? _SiteMappings.Last();
            }
            else
            {
                int? previousPage = _SiteMappings.LastOrDefault(f => f.ChapterNumber == currentChapter - 1)?.PageNumber;
                siteMap = _SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter - 1 && f.PageNumber == previousPage) ?? _SiteMappings.First();
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
