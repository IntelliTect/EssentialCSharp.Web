using System.Text.Json.Serialization;

namespace EssentialCSharp.Web.Models.Mcp;

public sealed class McpContextRequest
{
    [JsonPropertyName("query")] public string Query { get; set; } = string.Empty;
    [JsonPropertyName("top_k")] public int? TopK { get; set; }
    [JsonPropertyName("min_score")] public double? MinScore { get; set; }
    public string? Edition { get; set; }
    public int? Chapter { get; set; }
    public string? Section { get; set; }
}
