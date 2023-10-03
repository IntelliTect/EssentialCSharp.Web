using System.Configuration;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.CodeAnalysis;

namespace EssentialCSharp.Web.Controllers;

public class HomeController : Controller
{
    private readonly IConfiguration _Configuration;
    private readonly IWebHostEnvironment _HostingEnvironment;
    private readonly ISiteMappingService _SiteMappingService;
    private readonly ICaptchaService _CaptchaService;
    private readonly ILogger<HomeController> _Logger;

    public HomeController(ILogger<HomeController> logger, IWebHostEnvironment hostingEnvironment, ISiteMappingService siteMappingService, ICaptchaService captchaService, IConfiguration configuration)
    {
        _Logger = logger;
        _HostingEnvironment = hostingEnvironment;
        _SiteMappingService = siteMappingService;
        _CaptchaService = captchaService;
        _Configuration = configuration;
    }

    public IActionResult Index(string key)
    {
        // if no key (default case), then load up home page
        SiteMapping? siteMapping = SiteMapping.Find(key, _SiteMappingService.SiteMappings);

        if (string.IsNullOrEmpty(key))
        {
            return RedirectToAction(nameof(Home));
        }
        else if (siteMapping is not null)
        {
            string filePath = Path.Combine(_HostingEnvironment.ContentRootPath, Path.Combine(siteMapping.PagePath));
            HtmlDocument doc = new();
            doc.Load(filePath);
            string headHtml = doc.DocumentNode.Element("html").Element("head").InnerHtml;
            string html = doc.DocumentNode.Element("html").Element("body").InnerHtml;

            ViewBag.PageTitle = siteMapping.IndentLevel is 0 ? siteMapping.ChapterTitle + " " + siteMapping.RawHeading : siteMapping.RawHeading;
            ViewBag.NextPage = FlipPage(siteMapping!.ChapterNumber, siteMapping.PageNumber, true);
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

    [Route("/Announcements",
       Name = "Announcements")]
    public IActionResult Announcements()
    {
        ViewBag.PageTitle = "Announcements";
        return View();
    }

    [Route("/home",
    Name = "home")]
    public IActionResult Home()
    {
        return View();
    }

    [HttpPost, Route("/home",
        Name = "home")]
    public async Task<IActionResult> Home(IFormCollection collection)
    {
        string hCaptchaSecret = _Configuration.GetValue<string>("HCaptcha:Secret") ?? throw new InvalidOperationException("HCaptcha:Secret is null");
        string hCaptchaToken = collection["h-captcha-response"].ToString();
        HttpResponseMessage response = await _CaptchaService.Verify(hCaptchaSecret, hCaptchaToken, "b9235f58-3d8d-4394-ab8e-78b35a6d69c5");
        // The JSON should also return a field "success" as true
        // https://docs.hcaptcha.com/#verify-the-user-response-server-side
        //if (response.IsSuccessStatusCode)
        //{
        //    _Logger.HomeControllerSuccessfulCaptchaResponse(Json(response));
        //    return View();
        //}
        //else
        //{
        //    _Logger.HomeControllerSuccessfulCaptchaResponse(Json(response));

        //    return RedirectToAction(nameof(Error), new { errorMessage = "Captcha Failed. Forbidden", statusCode = 403 });
        //}
        return View();
    }

    private string FlipPage(int currentChapter, int currentPage, bool next)
    {
        if (_SiteMappingService.SiteMappings.Count == 0)
        {
            return "";
        }

        int page = -1;
        if (next)
        {
            page = 1;
        }

        SiteMapping? siteMap = _SiteMappingService.SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter && f.PageNumber == currentPage + page);

        if (siteMap is null)
        {
            if (next)
            {
                siteMap = _SiteMappingService.SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter + 1 && f.PageNumber == 1);
            }
            else
            {
                int? previousPage = _SiteMappingService.SiteMappings.LastOrDefault(f => f.ChapterNumber == currentChapter - 1)?.PageNumber;
                siteMap = _SiteMappingService.SiteMappings.FirstOrDefault(f => f.ChapterNumber == currentChapter - 1 && f.PageNumber == previousPage);
            }
            if (siteMap is null)
            {
                return "";
            }
        }
        return $"{siteMap.Key}#{siteMap.AnchorId}";
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
