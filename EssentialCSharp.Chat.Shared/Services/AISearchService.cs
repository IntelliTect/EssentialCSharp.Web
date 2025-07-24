using EssentialCSharp.Chat.Common.Models;
using Microsoft.Extensions.VectorData;

namespace EssentialCSharp.Chat.Common.Services;

public class AISearchService(VectorStore vectorStore, EmbeddingService embeddingService)
{
    // TODO: Implement Hybrid Search functionality, may need to switch db providers to support full text search?

    public async Task<IAsyncEnumerable<VectorSearchResult<BookContentChunk>>> ExecuteVectorSearch(string query, string? collectionName = null)
    {
        collectionName ??= EmbeddingService.CollectionName;

        VectorStoreCollection<string, BookContentChunk> collection = vectorStore.GetCollection<string, BookContentChunk>(collectionName);

        ReadOnlyMemory<float> searchVector = await embeddingService.GenerateEmbeddingAsync(query);

        var vectorSearchOptions = new VectorSearchOptions<BookContentChunk>
        {
            VectorProperty = x => x.TextEmbedding,
        };

        var searchResults = collection.SearchAsync(searchVector, options: vectorSearchOptions, top: 3);

        return searchResults;
    }
}
