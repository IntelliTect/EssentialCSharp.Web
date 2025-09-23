using EssentialCSharp.Web.Models;
using Microsoft.Extensions.Options;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EssentialCSharp.Web.Services;

public class TypesenseSearchService : ITypesenseSearchService
{
    private readonly HttpClient _httpClient;
    private readonly TypesenseOptions _options;
    private readonly ILogger<TypesenseSearchService> _logger;
    private readonly string _baseUrl;
    private const string CollectionName = "essentialcsharp_content";

    public TypesenseSearchService(IOptions<TypesenseOptions> options, ILogger<TypesenseSearchService> logger, HttpClient httpClient)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = httpClient;
        _baseUrl = $"{_options.Protocol}://{_options.Host}:{_options.Port}";

        _httpClient.DefaultRequestHeaders.Add("X-TYPESENSE-API-KEY", _options.ApiKey);
    }

    public async Task<bool> InitializeCollectionAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if collection already exists
            var checkResponse = await _httpClient.GetAsync($"{_baseUrl}/collections/{CollectionName}", cancellationToken);
            if (checkResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Collection {CollectionName} already exists", CollectionName);
                return true;
            }

            if (checkResponse.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogError("Failed to check collection existence: {StatusCode}", checkResponse.StatusCode);
                return false;
            }

            // Collection doesn't exist, create it
            _logger.LogInformation("Creating collection {CollectionName}", CollectionName);

            var schema = new
            {
                name = CollectionName,
                fields = new[]
                {
                    new { name = "id", type = "string", facet = false },
                    new { name = "title", type = "string", facet = false },
                    new { name = "content", type = "string", facet = false },
                    new { name = "url", type = "string", facet = false },
                    new { name = "chapter", type = "string", facet = true },
                    new { name = "section", type = "string", facet = true },
                    new { name = "tags", type = "string[]", facet = true },
                    new { name = "created_at", type = "int64", facet = false }
                },
                default_sorting_field = "created_at"
            };

            var json = JsonSerializer.Serialize(schema);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var createResponse = await _httpClient.PostAsync($"{_baseUrl}/collections", content, cancellationToken);
            if (createResponse.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully created collection {CollectionName}", CollectionName);
                return true;
            }

            var errorContent = await createResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to create collection {CollectionName}: {StatusCode} - {Error}", 
                CollectionName, createResponse.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize collection {CollectionName}", CollectionName);
            return false;
        }
    }

    public async Task<SearchResult> SearchAsync(string query, int page = 1, int perPage = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            var searchParams = new Dictionary<string, string>
            {
                ["q"] = query,
                ["query_by"] = "title,content,section,chapter",
                ["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["per_page"] = perPage.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["highlight_full_fields"] = "title,content",
                ["snippet_threshold"] = "30",
                ["num_typos"] = "2",
                ["drop_tokens_threshold"] = "1",
                ["sort_by"] = "_text_match:desc,created_at:desc"
            };

            var queryString = string.Join("&", searchParams.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
            var searchUrl = $"{_baseUrl}/collections/{CollectionName}/documents/search?{queryString}";

            var response = await _httpClient.GetAsync(searchUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Search failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
                return new SearchResult { Query = query, Page = page, PerPage = perPage };
            }

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var searchResult = JsonSerializer.Deserialize<TypesenseSearchResponse>(jsonContent);

            return new SearchResult
            {
                Results = searchResult?.hits?.Select(hit => hit.document).ToList() ?? [],
                TotalCount = searchResult?.out_of ?? 0,
                Page = page,
                PerPage = perPage,
                SearchTimeMs = searchResult?.search_time_ms ?? 0,
                Query = query
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search with query: {Query}", query);
            return new SearchResult { Query = query, Page = page, PerPage = perPage };
        }
    }

    public async Task<bool> IndexDocumentAsync(SearchDocument document, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = JsonSerializer.Serialize(document);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/collections/{CollectionName}/documents", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Successfully indexed document {DocumentId}", document.Id);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to index document {DocumentId}: {StatusCode} - {Error}", 
                document.Id, response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index document {DocumentId}", document.Id);
            return false;
        }
    }

    public async Task<bool> IndexDocumentsAsync(IEnumerable<SearchDocument> documents, CancellationToken cancellationToken = default)
    {
        try
        {
            var documentsList = documents.ToList();
            if (documentsList.Count == 0)
            {
                return true;
            }

            var json = JsonSerializer.Serialize(documentsList);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/collections/{CollectionName}/documents/import", content, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully indexed {Count} documents", documentsList.Count);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to index documents: {StatusCode} - {Error}", response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index documents");
            return false;
        }
    }

    public async Task<bool> DeleteDocumentAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync($"{_baseUrl}/collections/{CollectionName}/documents/{Uri.EscapeDataString(id)}", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Successfully deleted document {DocumentId}", id);
                return true;
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Failed to delete document {DocumentId}: {StatusCode} - {Error}", 
                id, response.StatusCode, errorContent);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete document {DocumentId}", id);
            return false;
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed");
            return false;
        }
    }
}

// Helper classes for Typesense API responses
public class TypesenseSearchResponse
{
#pragma warning disable CA1707 // Identifiers should not contain underscores - matches Typesense API format
    [JsonPropertyName("hits")]
    public TypesenseHit[]? hits { get; set; }
    
    [JsonPropertyName("out_of")]
    public int out_of { get; set; }
    
    [JsonPropertyName("search_time_ms")]
    public double search_time_ms { get; set; }
#pragma warning restore CA1707 // Identifiers should not contain underscores
}

public class TypesenseHit
{
    [JsonPropertyName("document")]
    public SearchDocument document { get; set; } = new();
}