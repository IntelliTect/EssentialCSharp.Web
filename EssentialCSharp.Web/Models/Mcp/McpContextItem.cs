namespace EssentialCSharp.Web.Models.Mcp;

public sealed class McpContextItem
{
    public string Id { get; set; } = string.Empty;
    public string Heading { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string FileName { get; set; } = string.Empty;
    public int? Chapter { get; set; }
    public double? Score { get; set; }
}
