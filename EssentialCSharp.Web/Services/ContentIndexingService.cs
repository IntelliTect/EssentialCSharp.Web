using EssentialCSharp.Web.Models;
using HtmlAgilityPack;
using System.Text.RegularExpressions;

namespace EssentialCSharp.Web.Services;

public interface IContentIndexingService
{
    Task<bool> IndexAllContentAsync(CancellationToken cancellationToken = default);
    Task<bool> IndexSiteMappingAsync(SiteMapping siteMapping, CancellationToken cancellationToken = default);
}

public class ContentIndexingService : IContentIndexingService
{
    private readonly ITypesenseSearchService _searchService;
    private readonly ISiteMappingService _siteMappingService;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<ContentIndexingService> _logger;

    public ContentIndexingService(
        ITypesenseSearchService searchService,
        ISiteMappingService siteMappingService,
        IWebHostEnvironment environment,
        ILogger<ContentIndexingService> logger)
    {
        _searchService = searchService;
        _siteMappingService = siteMappingService;
        _environment = environment;
        _logger = logger;
    }

    public async Task<bool> IndexAllContentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting to index all content");

            // Initialize the collection if it doesn't exist
            if (!await _searchService.InitializeCollectionAsync(cancellationToken))
            {
                _logger.LogError("Failed to initialize Typesense collection");
                return false;
            }

            var documents = new List<SearchDocument>();

            foreach (var siteMapping in _siteMappingService.SiteMappings)
            {
                var document = await CreateSearchDocumentAsync(siteMapping);
                if (document != null)
                {
                    documents.Add(document);
                }
            }

            if (documents.Count > 0)
            {
                var success = await _searchService.IndexDocumentsAsync(documents, cancellationToken);
                _logger.LogInformation("Indexed {Count} documents, success: {Success}", documents.Count, success);
                return success;
            }

            _logger.LogWarning("No documents to index");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index all content");
            return false;
        }
    }

    public async Task<bool> IndexSiteMappingAsync(SiteMapping siteMapping, CancellationToken cancellationToken = default)
    {
        try
        {
            var document = await CreateSearchDocumentAsync(siteMapping);
            if (document == null)
            {
                return false;
            }

            return await _searchService.IndexDocumentAsync(document, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index site mapping {Key}", siteMapping.PrimaryKey);
            return false;
        }
    }

    private async Task<SearchDocument?> CreateSearchDocumentAsync(SiteMapping siteMapping)
    {
        try
        {
            var filePath = Path.Combine(_environment.ContentRootPath, Path.Combine(siteMapping.PagePath));
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File not found: {FilePath}", filePath);
                return null;
            }

            var htmlContent = await File.ReadAllTextAsync(filePath);
            var doc = new HtmlDocument();
            doc.LoadHtml(htmlContent);

            // Extract content from body
            var bodyNode = doc.DocumentNode.SelectSingleNode("//body");
            if (bodyNode == null)
            {
                _logger.LogWarning("No body content found in {FilePath}", filePath);
                return null;
            }

            // Remove script and style elements
            var scriptsAndStyles = bodyNode.SelectNodes("//script | //style");
            if (scriptsAndStyles != null)
            {
                foreach (var node in scriptsAndStyles)
                {
                    node.Remove();
                }
            }

            // Extract plain text content
            var textContent = bodyNode.InnerText;
            var cleanContent = CleanTextContent(textContent);

            // Create tags based on the content
            var tags = new List<string>();
            if (!string.IsNullOrEmpty(siteMapping.ChapterTitle))
            {
                tags.Add($"chapter-{siteMapping.ChapterNumber}");
            }

            // Extract URL from the first key
            var url = $"/{siteMapping.Keys.First()}";
            if (!string.IsNullOrEmpty(siteMapping.AnchorId))
            {
                url += $"#{siteMapping.AnchorId}";
            }

            return new SearchDocument
            {
                Id = siteMapping.PrimaryKey,
                Title = siteMapping.RawHeading ?? siteMapping.ChapterTitle ?? "Unknown",
                Content = cleanContent,
                Url = url,
                Chapter = $"Chapter {siteMapping.ChapterNumber}: {siteMapping.ChapterTitle}",
                Section = siteMapping.RawHeading ?? string.Empty,
                Tags = tags,
                CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create search document for {Key}", siteMapping.PrimaryKey);
            return null;
        }
    }

    private static string CleanTextContent(string htmlText)
    {
        if (string.IsNullOrEmpty(htmlText))
        {
            return string.Empty;
        }

        // Decode HTML entities
        var decodedText = HtmlEntity.DeEntitize(htmlText);

        // Remove extra whitespace and normalize line breaks
        var cleanText = Regex.Replace(decodedText, @"\s+", " ");
        
        // Remove leading/trailing whitespace
        cleanText = cleanText.Trim();

        // Limit content length for search indexing (Typesense has limits)
        if (cleanText.Length > 10000)
        {
            cleanText = cleanText[..10000] + "...";
        }

        return cleanText;
    }
}