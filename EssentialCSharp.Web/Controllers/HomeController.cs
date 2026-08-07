using DotnetSitemapGenerator;
using EssentialCSharp.Web.Constants;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Helpers;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Controllers;

public class HomeController(ILogger<HomeController> logger, IWebHostEnvironment hostingEnvironment, ISiteMappingService siteMappingService, IHttpContextAccessor httpContextAccessor, IRouteConfigurationService routeConfigurationService, IOptions<SiteSettings> siteSettings) : BaseController(routeConfigurationService, httpContextAccessor)
{
    [EnableRateLimiting("content")]
    public IActionResult Index()
    {
        string? key = Request.Path.Value?.TrimStart('/');

        // if no key (default case), then load up home page
        SiteMapping? siteMapping = siteMappingService.SiteMappings.Find(key);

        if (string.IsNullOrEmpty(key))
        {
            return RedirectToAction(nameof(Home));
        }
        else if (siteMapping is not null)
        {
            string filePath = Path.Join(hostingEnvironment.ContentRootPath, Path.Join(siteMapping.PagePath));
            HtmlDocument doc = new();
            doc.Load(filePath);
            string headHtml = doc.DocumentNode.Element("html").Element("head").InnerHtml;
            string html = doc.DocumentNode.Element("html").Element("body").InnerHtml;

            ViewBag.PageTitle = siteMapping.IndentLevel is 0 ? siteMapping.ChapterTitle + " " + siteMapping.RawHeading : siteMapping.RawHeading;
            ViewBag.NextPage = FlipPage(siteMapping!.ChapterNumber, siteMapping.PageNumber, true);
            ViewBag.CurrentPageKey = siteMapping.PrimaryKey;
            ViewBag.PreviousPage = FlipPage(siteMapping.ChapterNumber, siteMapping.PageNumber, false);
            ViewBag.HeadContents = headHtml;
            ViewBag.Contents = html;
            return View();
        }
        else
        {
            return RedirectToAction(nameof(Error), new { errorMessage = "Specified page not found, please check your spelling and try again", statusCode = 404 });
        }
    }

    [Route(RouteConstants.StaticPages.TermsOfService,
       Name = "TermsOfService")]
    public IActionResult TermsOfService()
    {
        ViewBag.PageTitle = "Terms Of Service";
        return View();
    }

    [Route(RouteConstants.StaticPages.Announcements, Name = "Announcements")]
    public IActionResult Announcements()
    {
        ViewBag.PageTitle = "Announcements";
        return View();
    }

    [Route(RouteConstants.StaticPages.About, Name = "about")]
    public IActionResult About()
    {
        ViewBag.PageTitle = "About";
        return View();
    }

    [Route(RouteConstants.StaticPages.Home, Name = "home")]
    public IActionResult Home()
    {
        return View();
    }

    [Route(RouteConstants.StaticPages.Guidelines, Name = "guidelines")]
    public IActionResult Guidelines()
    {
        ViewBag.PageTitle = "Coding Guidelines";
        FileInfo fileInfo = new(Path.Join(hostingEnvironment.ContentRootPath, "Guidelines", "guidelines.json"));
        if (!fileInfo.Exists)
        {
            return RedirectToAction(nameof(Error), new { errorMessage = "Guidelines could not be found", statusCode = 404 });
        }
        ViewBag.Guidelines = fileInfo.ReadGuidelineJsonFromInputDirectory(logger);
        ViewBag.GuidelinesUrl = Request.Path.Value;
        return View();
    }

    [Route("/sitemap.xml")]
    [OutputCache(Duration = 3600)]
    [EnableRateLimiting("content")]
    public IActionResult SitemapXml()
    {
        SitemapXmlHelpers.GenerateSitemapXml(siteMappingService.SiteMappings, RouteConfigurationService, siteSettings.Value.BaseUrl, out var nodes);
        return new SitemapProvider().CreateSitemap(new SitemapModel(nodes));
    }

    private string FlipPage(int currentChapter, int currentPage, bool next)
    {
        if (siteMappingService.SiteMappings.Count == 0)
        {
            return "";
        }

        int page = -1;
        if (next)
        {
            page = 1;
        }

        SiteMapping? siteMap = siteMappingService.SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter && f.PageNumber == currentPage + page);

        if (siteMap is null)
        {
            if (next)
            {
                siteMap = siteMappingService.SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter + 1 && f.PageNumber == 1);
            }
            else
            {
                int? previousPage = siteMappingService.SiteMappings.LastOrDefault(f => f.ChapterNumber == currentChapter - 1)?.PageNumber;
                siteMap = siteMappingService.SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter - 1 && f.PageNumber == previousPage);
            }
            if (siteMap is null)
            {
                return "";
            }
        }
        return $"{siteMap.Keys.First()}#{siteMap.AnchorId}";
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error(string? errorMessage = null, int statusCode = 404)
    {
        if (statusCode is < 400 or > 599)
        {
            statusCode = 500;
        }
        Response.StatusCode = statusCode;
        ViewBag.StatusCode = statusCode;
        ViewBag.ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : $"{statusCode}: {errorMessage}";
        ViewBag.PageTitle = $"Error-{statusCode}";
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
