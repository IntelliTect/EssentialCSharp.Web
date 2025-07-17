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
    public static string CollectionName { get; } = "markdown_chunks";

    /// <summary>
    /// Generate an embedding for the given text.
    /// </summary>
    /// <param name="text">The text to generate an embedding for.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A search vector as ReadOnlyMemory&lt;float&gt;.</returns>
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        return await textEmbeddingGenerationService.GenerateEmbeddingAsync(text, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Generate an embedding for each text paragraph and upload it to the specified collection.
    /// </summary>
    /// <param name="collectionName">The name of the collection to upload the text paragraphs to.</param>
    /// <returns>An async task.</returns>
    public async Task GenerateBookContentEmbeddingsAndUploadToVectorStore(IEnumerable<BookContentChunk> bookContents, CancellationToken cancellationToken, string? collectionName = null)
    {
        collectionName ??= CollectionName;

        var collection = vectorStore.GetCollection<string, BookContentChunk>(collectionName);
        await collection.EnsureCollectionExistsAsync(cancellationToken);

        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = 5,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(bookContents, parallelOptions, async (chunk, cancellationToken) =>
        {
            // Generate the text embedding using the new method.
            chunk.TextEmbedding = await GenerateEmbeddingAsync(chunk.ChunkText, cancellationToken);

            await collection.UpsertAsync(chunk, cancellationToken);
            Console.WriteLine($"Uploaded chunk '{chunk.Id}' to collection '{collectionName}' for file '{chunk.FileName}' with heading '{chunk.Heading}'.");
        });
        Console.WriteLine($"Successfully generated embeddings and uploaded {bookContents.Count()} chunks to collection '{collectionName}'.");
    }
}
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to proceed.
