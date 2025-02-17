using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;

namespace EssentialCSharp.Web.Controllers;

public class HomeController(ILogger<HomeController> logger, IWebHostEnvironment hostingEnvironment, ISiteMappingService siteMappingService, IHttpContextAccessor httpContextAccessor) : Controller
{
    public IActionResult Index(string key)
    {
        // if no key (default case), then load up home page
        SiteMapping? siteMapping = siteMappingService.SiteMappings.Find(key);

        if (string.IsNullOrEmpty(key))
        {
            return RedirectToAction(nameof(Home));
        }
        else if (siteMapping is not null)
        {
            string filePath = Path.Combine(hostingEnvironment.ContentRootPath, Path.Combine(siteMapping.PagePath));
            HtmlDocument doc = new();
            doc.Load(filePath);
            string headHtml = doc.DocumentNode.Element("html").Element("head").InnerHtml;
            string html = doc.DocumentNode.Element("html").Element("body").InnerHtml;

            ViewBag.PageTitle = siteMapping.IndentLevel is 0 ? siteMapping.ChapterTitle + " " + siteMapping.RawHeading : siteMapping.RawHeading;
            ViewBag.NextPage = FlipPage(siteMapping!.ChapterNumber, siteMapping.PageNumber, true);
            ViewBag.PreviousPage = FlipPage(siteMapping.ChapterNumber, siteMapping.PageNumber, false);
            ViewBag.HeadContents = headHtml;
            ViewBag.Contents = html;
            // Set the referral Id for use in the front end if available
            ViewBag.ReferralId = httpContextAccessor.HttpContext?.User?.Claims?.FirstOrDefault(f => f.Type == ClaimsExtensions.ReferrerIdClaimType)?.Value;
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
        FileInfo fileInfo = new(Path.Combine(hostingEnvironment.ContentRootPath, "Guidelines", "guidelines.json"));
        if (!fileInfo.Exists)
        {
            return RedirectToAction(nameof(Error), new { errorMessage = "Guidelines could not be found", statusCode = 404 });
        }
        ViewBag.Guidelines = fileInfo.ReadGuidelineJsonFromInputDirectory(logger);
        ViewBag.GuidelinesUrl = Request.Path.Value;
        return View();
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
        Response.StatusCode = statusCode;
        ViewBag.ErrorMessage = $"{statusCode}: {errorMessage}";
        ViewBag.PageTitle = $"Error-{statusCode}";
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
