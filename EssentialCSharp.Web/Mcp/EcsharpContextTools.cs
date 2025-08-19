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
    public static async IAsyncEnumerable<string> GetContext(
        [Description("Vector search service")] AISearchService search,
        [Description("Natural language query")] string query,
        [Description("Number of chunks to return (1-10)")] int top_k = 5,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        top_k = Math.Clamp(top_k, 1, 10);
        var results = await search.ExecuteVectorSearch(query, top_k);
        await foreach (var r in results.WithCancellation(cancellationToken))
        {
            // Yield text content blocks; clients can request more via the REST controller if they need metadata
            yield return r.Record.ChunkText;
        }
    }
}
