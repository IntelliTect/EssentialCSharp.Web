using EssentialCSharp.Chat.Common.Models;
using Microsoft.Extensions.VectorData;

namespace EssentialCSharp.Chat.Common.Services;

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
public class AISearchService(VectorStore vectorStore, EmbeddingService embeddingService)
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
{
    // TODO: Implement Hybrid Search functionality, may need to switch db providers to support full text search?
    //public async Task ExecuteHybridSearch(string query, string? collectionName = null)
    //{
    //    collectionName ??= EmbeddingService.CollectionName;

    //    IKeywordHybridSearchable<BookContentChunk> collection = (IKeywordHybridSearchable<BookContentChunk>)vectorStore.GetCollection<string, BookContentChunk>(collectionName);

    //    ReadOnlyMemory<float> searchVector = await embeddingService.GenerateEmbeddingAsync(query);

    //    var hybridSearchOptions = new HybridSearchOptions<BookContentChunk>
    //    {

    //    };

    //    var searchResults = await collection.HybridSearchAsync  (searchVector, ["C#"], top: 3);
    //    foreach (var result in results)
    //    {
    //        Console.WriteLine($"Found chunk: {result.Value.Heading} in file {result.Value.FileName}");
    //    }
    //}

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
