using System.Text.Json.Serialization;

namespace EssentialCSharp.Web.Models;

public class SearchDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("chapter")]
    public string Chapter { get; set; } = string.Empty;

    [JsonPropertyName("section")]
    public string Section { get; set; } = string.Empty;

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];

    [JsonPropertyName("created_at")]
    public long CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

public class SearchResult
{
    public List<SearchDocument> Results { get; set; } = [];
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PerPage { get; set; }
    public double SearchTimeMs { get; set; }
    public string Query { get; set; } = string.Empty;
}

public class SearchRequest
{
    public string Query { get; set; } = string.Empty;
    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 10;
    public List<string> Filters { get; set; } = [];
}