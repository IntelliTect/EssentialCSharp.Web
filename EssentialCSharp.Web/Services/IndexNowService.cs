namespace EssentialCSharp.Web.Services;

public interface IIndexNowService
{
    Task NotifyUrlAsync(string url);
    Task NotifyUrlsAsync(IEnumerable<string> urls);
}

public class IndexNowService : IIndexNowService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<IndexNowService> _logger;
    
    // IndexNow endpoints for different search engines
    private readonly string[] _searchEngineEndpoints = 
    {
        "https://api.indexnow.org/IndexNow", // Generic endpoint
        "https://www.bing.com/IndexNow",     // Bing
        "https://search.seznam.cz/IndexNow", // Seznam.cz
        "https://searchadvisor.naver.com/indexnow" // Naver
    };

    public IndexNowService(HttpClient httpClient, IConfiguration configuration, ILogger<IndexNowService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task NotifyUrlAsync(string url)
    {
        await NotifyUrlsAsync(new[] { url });
    }

    public async Task NotifyUrlsAsync(IEnumerable<string> urls)
    {
        string? key = _configuration["IndexNow:Key"];
        string? baseUrl = _configuration["IndexNow:BaseUrl"];

        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(baseUrl))
        {
            _logger.LogWarning("IndexNow key or base URL not configured. Skipping notification.");
            return;
        }

        var urlList = urls.ToList();
        if (urlList.Count == 0)
        {
            return;
        }

        var payload = new
        {
            host = baseUrl.Replace("https://", "").Replace("http://", ""),
            key = key,
            keyLocation = $"{baseUrl}/{key}.txt",
            urlList = urlList
        };

        foreach (string endpoint in _searchEngineEndpoints)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("Successfully notified {Endpoint} about {UrlCount} URLs", endpoint, urlList.Count);
                }
                else
                {
                    _logger.LogWarning("Failed to notify {Endpoint}. Status: {StatusCode}", endpoint, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error notifying {Endpoint} about URL changes", endpoint);
            }
        }
    }
}