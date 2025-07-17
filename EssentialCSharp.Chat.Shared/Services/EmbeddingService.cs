using EssentialCSharp.Chat.Common.Models;
using Microsoft.Extensions.VectorData;
using Microsoft.SemanticKernel.Embeddings;


namespace EssentialCSharp.Chat.Common.Services;

/// <summary>
/// Service for generating embeddings for markdown chunks using Azure OpenAI
/// </summary>
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
public class EmbeddingService(VectorStore vectorStore, ITextEmbeddingGenerationService textEmbeddingGenerationService)
{
    /// <summary>
    /// Generate an embedding for each text paragraph and upload it to the specified collection.
    /// </summary>
    /// <param name="collectionName">The name of the collection to upload the text paragraphs to.</param>
    /// <param name="textParagraphs">The text paragraphs to upload.</param>
    /// <returns>An async task.</returns>
    public async Task GenerateEmbeddingsAndUpload(string collectionName, IEnumerable<BookContentChunk> bookContents)
    {
        var collection = vectorStore.GetCollection<string, BookContentChunk>(collectionName);
        await collection.EnsureCollectionExistsAsync();

        foreach (var chunk in bookContents)
        {
            // Generate the text embedding.
            chunk.TextEmbedding = await textEmbeddingGenerationService.GenerateEmbeddingAsync(chunk.ChunkText);

            await collection.UpsertAsync(chunk);
        }
        Console.WriteLine($"Successfully generated embeddings and uploaded {bookContents.Count()} chunks to collection '{collectionName}'.");
    }
}
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
