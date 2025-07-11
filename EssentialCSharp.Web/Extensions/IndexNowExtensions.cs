using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Extensions;

public static class IndexNowExtensions
{
    /// <summary>
    /// Triggers IndexNow notifications for a single URL
    /// </summary>
    public static async Task NotifyIndexNowAsync(this IServiceProvider services, string relativeUrl)
    {
        var indexNowService = services.GetService<IIndexNowService>();
        var configuration = services.GetService<IConfiguration>();
        
        if (indexNowService != null && configuration != null)
        {
            string? baseUrl = configuration["IndexNow:BaseUrl"];
            if (!string.IsNullOrEmpty(baseUrl))
            {
                string fullUrl = $"{baseUrl.TrimEnd('/')}/{relativeUrl.TrimStart('/')}";
                await indexNowService.NotifyUrlAsync(fullUrl);
            }
        }
    }

    /// <summary>
    /// Triggers IndexNow notifications for multiple URLs
    /// </summary>
    public static async Task NotifyIndexNowAsync(this IServiceProvider services, IEnumerable<string> relativeUrls)
    {
        var indexNowService = services.GetService<IIndexNowService>();
        var configuration = services.GetService<IConfiguration>();
        
        if (indexNowService != null && configuration != null)
        {
            string? baseUrl = configuration["IndexNow:BaseUrl"];
            if (!string.IsNullOrEmpty(baseUrl))
            {
                var fullUrls = relativeUrls.Select(url => $"{baseUrl.TrimEnd('/')}/{url.TrimStart('/')}");
                await indexNowService.NotifyUrlsAsync(fullUrls);
            }
        }
    }

    /// <summary>
    /// Triggers IndexNow notification for all sitemap URLs
    /// </summary>
    public static async Task NotifyAllSitemapUrlsAsync(this IServiceProvider services)
    {
        var siteMappingService = services.GetService<ISiteMappingService>();
        
        if (siteMappingService != null)
        {
            var urls = siteMappingService.SiteMappings
                .Where(x => x.IncludeInSitemapXml)
                .GroupBy(x => x.Keys.First())
                .Select(g => g.Key)
                .ToList();

            // Add home page
            urls.Insert(0, "");

            await services.NotifyIndexNowAsync(urls);
        }
    }
}