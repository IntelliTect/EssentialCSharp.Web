using EssentialCSharp.Chat.Common.Models;
using Microsoft.Extensions.VectorData;

namespace EssentialCSharp.Chat.Common.Services;

public class AISearchService(VectorStore vectorStore, EmbeddingService embeddingService)
{
    // TODO: Implement Hybrid Search functionality, may need to switch db providers to support full text search?

    public async Task<IAsyncEnumerable<VectorSearchResult<BookContentChunk>>> ExecuteVectorSearch(string query, string? collectionName = null)
    {
        return await ExecuteVectorSearch(query, 3, collectionName);
    }

    /// <summary>
    /// Execute vector search with a caller-specified top-K.
    /// </summary>
    /// <param name="query">Natural language query.</param>
    /// <param name="top">Number of results to return (bounded by the vector store and service limits).</param>
    /// <param name="collectionName">Optional collection name; defaults to EmbeddingService.CollectionName.</param>
    public async Task<IAsyncEnumerable<VectorSearchResult<BookContentChunk>>> ExecuteVectorSearch(string query, int top, string? collectionName = null)
    {
        collectionName ??= EmbeddingService.CollectionName;

        VectorStoreCollection<string, BookContentChunk> collection = vectorStore.GetCollection<string, BookContentChunk>(collectionName);

        ReadOnlyMemory<float> searchVector = await embeddingService.GenerateEmbeddingAsync(query);

        var vectorSearchOptions = new VectorSearchOptions<BookContentChunk>
        {
            VectorProperty = x => x.TextEmbedding,
        };

        var topClamped = Math.Max(1, Math.Min(top, 10));
        var searchResults = collection.SearchAsync(searchVector, options: vectorSearchOptions, top: topClamped);

        return searchResults;
    }
}
