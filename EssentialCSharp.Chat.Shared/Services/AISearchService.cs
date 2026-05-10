using System.Diagnostics;
using EssentialCSharp.Chat.Common.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Npgsql;

namespace EssentialCSharp.Chat.Common.Services;

public partial class AISearchService(
    VectorStore vectorStore,
    EmbeddingService embeddingService,
    ILogger<AISearchService> logger)
{
    // TODO: Implement Hybrid Search functionality, may need to switch db providers to support full text search?

    public const int DefaultSearchTop = 5;
    public const int MaxSearchTop = 10;

    public async Task<IReadOnlyList<VectorSearchResult<BookContentChunk>>> ExecuteVectorSearch(
        string query, string? collectionName = null, int top = DefaultSearchTop, CancellationToken cancellationToken = default)
    {
        top = Math.Clamp(top, 1, MaxSearchTop);
        collectionName ??= EmbeddingService.CollectionName;

        VectorStoreCollection<string, BookContentChunk> collection = vectorStore.GetCollection<string, BookContentChunk>(collectionName);

        ReadOnlyMemory<float> searchVector = await embeddingService.GenerateEmbeddingAsync(query, cancellationToken);

        var vectorSearchOptions = new VectorSearchOptions<BookContentChunk>
        {
            VectorProperty = x => x.TextEmbedding,
        };

        for (int attempt = 0; attempt <= 1; attempt++)
        {
            try
            {
                // Fetch more candidates than needed so we can deduplicate by heading.
                // Multiple chunks from the same section share the same Heading; without dedup
                // all top-N results could come from one long section, reducing context diversity.
                int candidates = top * 3;

                var candidatesList = new List<VectorSearchResult<BookContentChunk>>();
                await foreach (var result in collection.SearchAsync(searchVector, options: vectorSearchOptions, top: candidates, cancellationToken: cancellationToken))
                {
                    candidatesList.Add(result);
                }

                // Keep only the highest-scoring chunk per unique heading, then take the globally
                // top-N by score. GroupBy on a materialized list preserves insertion (score desc)
                // order, but we make the ordering explicit via OrderByDescending so the result
                // is correct regardless of provider sort guarantees.
                // MaxBy on a non-empty IGrouping never returns null; ! asserts this invariant.
                var results = candidatesList
                    .GroupBy(r => r.Record.Heading)
                    .Select(g => g.MaxBy(r => r.Score)!)
                    .OrderByDescending(r => r.Score)
                    .Take(top)
                    .ToList();

                return results;
            }
            catch (PostgresException ex) when (ex.SqlState == "28000" && attempt == 0)
            {
                // The pooled connection held an expired Entra ID token. Npgsql automatically
                // removes the broken connection from the pool on error — no manual pool clearing
                // needed (clearing would evict all healthy connections, hurting concurrent users).
                // The retry opens a fresh physical connection, which calls UsePasswordProvider
                // and gets a new token from DefaultAzureCredential.
                LogEntraIdTokenExpired(logger, ex);
            }
        }

        throw new UnreachableException("Retry loop exited without returning or throwing.");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Entra ID token expired on pooled connection (SqlState 28000); retrying once.")]
    private static partial void LogEntraIdTokenExpired(ILogger<AISearchService> logger, Exception exception);
}
