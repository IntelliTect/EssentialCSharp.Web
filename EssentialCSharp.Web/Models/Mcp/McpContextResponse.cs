namespace EssentialCSharp.Web.Models.Mcp;

public sealed class McpContextResponse
{
    public required McpContextItem[] Items { get; init; }
    public int Total { get; init; }
    public string License { get; init; } = "© Essential C# — used for QA grounding only";
}
