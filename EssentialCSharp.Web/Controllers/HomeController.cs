using DotnetSitemapGenerator;
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

public partial class HomeController(ILogger<HomeController> logger, IWebHostEnvironment hostingEnvironment, ISiteMappingService siteMappingService, IHttpContextAccessor httpContextAccessor, IRouteConfigurationService routeConfigurationService, IOptions<SiteSettings> siteSettings) : BaseController(routeConfigurationService, httpContextAccessor)
{
    // Keep this map in sync with files that materially affect each route's rendered content.
    private static readonly IReadOnlyDictionary<string, string[]> StaticRouteContentFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["/home"] = ["Views\\Home\\Home.cshtml", "Models\\AnnouncementCatalog.cs"],
        ["/about"] = ["Views\\Home\\About.cshtml"],
        ["/announcements"] = ["Views\\Home\\Announcements.cshtml", "Models\\AnnouncementCatalog.cs"],
        ["/termsofservice"] = ["Views\\Home\\TermsOfService.cshtml"],
        ["/guidelines"] = ["Views\\Home\\Guidelines.cshtml", "Guidelines\\guidelines.json"]
    };

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

    [Route("/TermsOfService",
       Name = "TermsOfService")]
    public IActionResult TermsOfService()
    {
        ViewBag.PageTitle = "Terms Of Service";
        return View();
    }

    [Route("/Announcements", Name = "Announcements")]
    public IActionResult Announcements()
    {
        ViewBag.PageTitle = "Announcements";
        return View();
    }

    [Route("/about", Name = "about")]
    public IActionResult About()
    {
        ViewBag.PageTitle = "About";
        return View();
    }

    [Route("/home", Name = "home")]
    public IActionResult Home()
    {
        return View();
    }

    [Route("/guidelines", Name = "guidelines")]
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
        SitemapXmlHelpers.GenerateSitemapXml(
            siteMappingService.SiteMappings,
            RouteConfigurationService,
            siteSettings.Value.BaseUrl,
            GetStaticRouteLastModifiedDates(),
            out var nodes);
        return new SitemapProvider().CreateSitemap(new SitemapModel(nodes));
    }

    private Dictionary<string, DateTime> GetStaticRouteLastModifiedDates()
    {
        var routeLastModifiedDates = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach ((var route, var sourceFiles) in StaticRouteContentFiles)
        {
            DateTime? maxLastModified = null;
            foreach (var sourceFile in sourceFiles)
            {
                var sourceFilePath = Path.Join(hostingEnvironment.ContentRootPath, sourceFile);
                if (!System.IO.File.Exists(sourceFilePath))
                {
                    LogSitemapMappedFileMissing(logger, route, sourceFilePath);
                    continue;
                }

                var sourceFileLastWriteTime = System.IO.File.GetLastWriteTimeUtc(sourceFilePath);
                if (sourceFileLastWriteTime <= DateTime.UnixEpoch)
                {
                    continue;
                }

                maxLastModified = maxLastModified is null
                    ? sourceFileLastWriteTime
                    : sourceFileLastWriteTime > maxLastModified.Value ? sourceFileLastWriteTime : maxLastModified.Value;
            }

            if (maxLastModified is DateTime routeLastModified)
            {
                routeLastModifiedDates[route] = routeLastModified;
            }
        }

        return routeLastModifiedDates;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sitemap mapped file missing for route {Route}: {FilePath}")]
    private static partial void LogSitemapMappedFileMissing(ILogger<HomeController> logger, string route, string filePath);

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
