using EssentialCSharp.Web.Services;
using EssentialCSharp.Web.Extensions;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;
using System.Xml;

namespace EssentialCSharp.Web.Controllers;

public class SitemapController(ISiteMappingService siteMappingService, IConfiguration configuration) : Controller
{
    private readonly ISiteMappingService _siteMappingService = siteMappingService;
    private readonly IConfiguration _configuration = configuration;

    [Route("sitemap.xml")]
    [ResponseCache(Duration = 3600)] // Cache for 1 hour
    public IActionResult Sitemap()
    {
        string baseUrl = GetBaseUrl();
        string xmlContent = GenerateSitemapXml(baseUrl);
        
        return Content(xmlContent, "application/xml", Encoding.UTF8);
    }

    [Route("indexnow")]
    [HttpPost]
    public IActionResult IndexNow([FromBody] IndexNowRequest request)
    {
        // Validate the request
        if (request?.Url == null || !Uri.IsWellFormedUriString(request.Url, UriKind.Absolute))
        {
            return BadRequest("Invalid URL");
        }

        // For now, just return OK. The actual notification will be handled by the IndexNow service
        return Ok();
    }

    [Route("{keyFileName}.txt")]
    public IActionResult IndexNowKey(string keyFileName)
    {
        string? indexNowKey = _configuration["IndexNow:Key"];
        
        if (string.IsNullOrEmpty(indexNowKey))
        {
            return NotFound();
        }

        // The key file name should match the configured key
        if (keyFileName != indexNowKey)
        {
            return NotFound();
        }

        return Content(indexNowKey, "text/plain");
    }

    [Route("api/notify-indexnow")]
    [HttpPost]
    public async Task<IActionResult> NotifyIndexNow([FromServices] IServiceProvider serviceProvider)
    {
        try
        {
            await serviceProvider.NotifyAllSitemapUrlsAsync();
            return Ok(new { message = "IndexNow notifications sent successfully" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private string GetBaseUrl()
    {
        string scheme = Request.Scheme;
        string host = Request.Host.Value;
        return $"{scheme}://{host}";
    }

    private string GenerateSitemapXml(string baseUrl)
    {
        var siteMappings = _siteMappingService.SiteMappings
            .Where(x => x.IncludeInSitemapXml)
            .GroupBy(x => x.Keys.First())
            .Select(g => g.First()) // Take first mapping for each unique key
            .OrderBy(x => x.ChapterNumber)
            .ThenBy(x => x.PageNumber);

        var xmlBuilder = new StringBuilder();
        xmlBuilder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xmlBuilder.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        // Add home page
        xmlBuilder.AppendLine("  <url>");
        xmlBuilder.Append(CultureInfo.InvariantCulture, $"    <loc>{baseUrl}/</loc>");
        xmlBuilder.AppendLine();
        xmlBuilder.AppendLine("    <changefreq>weekly</changefreq>");
        xmlBuilder.AppendLine("    <priority>1.0</priority>");
        xmlBuilder.AppendLine("  </url>");

        // Add all site mappings
        foreach (var mapping in siteMappings)
        {
            string url = $"{baseUrl}/{mapping.Keys.First()}";
            xmlBuilder.AppendLine("  <url>");
            xmlBuilder.Append(CultureInfo.InvariantCulture, $"    <loc>{XmlEncode(url)}</loc>");
            xmlBuilder.AppendLine();
            xmlBuilder.AppendLine("    <changefreq>monthly</changefreq>");
            xmlBuilder.AppendLine("    <priority>0.8</priority>");
            xmlBuilder.AppendLine("  </url>");
        }

        xmlBuilder.AppendLine("</urlset>");
        return xmlBuilder.ToString();
    }

    private static string XmlEncode(string text)
    {
        return System.Security.SecurityElement.Escape(text) ?? text;
    }
}

public class IndexNowRequest
{
    public string? Url { get; set; }
    public string? Key { get; set; }
    public string? KeyLocation { get; set; }
}