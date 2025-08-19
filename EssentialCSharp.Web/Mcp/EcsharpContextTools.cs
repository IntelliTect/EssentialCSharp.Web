using System.ComponentModel;
using EssentialCSharp.Chat.Common.Models;
using EssentialCSharp.Chat.Common.Services;
using ModelContextProtocol.Server;
using System.Diagnostics.CodeAnalysis;

namespace EssentialCSharp.Web.Mcp;

[McpServerToolType]
public static class EcsharpContextTools
{
    [McpServerTool(Name = "get_ecsharp_context"), Description("Search Essential C# book corpus and return citeable chunks.")]
    public static async Task<List<string>> GetContext(
        [Description("Vector search service")] AISearchService search,
        [Description("Natural language query")] string query,
        [Description("Number of chunks to return (1-10)")] int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        topK = Math.Clamp(topK, 1, 10);
        var results = await search.ExecuteVectorSearch(query, topK);
        var chunks = new List<string>();
        await foreach (var r in results.WithCancellation(cancellationToken))
        {
            // Collect text content blocks; clients can request more via the REST controller if they need metadata
            chunks.Add(r.Record.ChunkText);
        }
        return chunks;
    }
}
