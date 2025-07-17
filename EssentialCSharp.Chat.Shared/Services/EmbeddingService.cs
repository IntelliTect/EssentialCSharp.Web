namespace EssentialCSharp.Chat.Common.Services;

///// <summary>
///// Service for generating embeddings for markdown chunks using Azure OpenAI
///// Following Microsoft best practices for Semantic Kernel embedding generation
///// </summary>
//public class EmbeddingService
//{
//    private readonly IEmbeddingGenerator<string, Embedding<float>> _EmbeddingGenerator;

//    public EmbeddingService(IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
//    {
//        _EmbeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
//    }

//    /// <summary>
//    /// Generate embeddings for a collection of markdown chunks
//    /// </summary>
//    /// <param name="chunks">The chunks to generate embeddings for</param>
//    /// <param name="cancellationToken">Cancellation token</param>
//    /// <returns>The chunks with embeddings populated</returns>
//    public async Task<IList<BookContentChunk>> GenerateEmbeddingsAsync(
//        IList<BookContentChunk> chunks,
//        CancellationToken cancellationToken = default)
//    {
//        if (!chunks.Any())
//            return chunks;

//        Console.WriteLine($"Generating embeddings for {chunks.Count} chunks...");

//        try
//        {
//            // Generate embeddings one by one to avoid rate limits
//            for (int i = 0; i < chunks.Count; i++)
//            {
//                var chunk = chunks[i];
//                if (string.IsNullOrWhiteSpace(chunk.ChunkText))
//                    continue;

//                var embedding = await _EmbeddingGenerator.GenerateEmbeddingAsync(
//                    chunk.ChunkText,
//                    options: null,
//                    cancellationToken);
//                chunk.TextEmbedding = new ReadOnlyMemory<float>(embedding.Vector.ToArray());

//                // Show progress
//                if ((i + 1) % 10 == 0)
//                {
//                    Console.WriteLine($"   Generated embeddings for {i + 1}/{chunks.Count} chunks");
//                }
//            }

//            var successfulEmbeddings = chunks.Count(c => c.TextEmbedding.HasValue);
//            Console.WriteLine($"✅ Successfully generated embeddings for {successfulEmbeddings}/{chunks.Count} chunks");

//            if (successfulEmbeddings > 0)
//            {
//                Console.WriteLine($"   Vector dimensions: {chunks.First(c => c.TextEmbedding.HasValue).TextEmbedding!.Value.Length}");
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Error generating embeddings: {ex.Message}");
//            throw;
//        }

//        return chunks;
//    }

//    /// <summary>
//    /// Generate embedding for a single text
//    /// </summary>
//    /// <param name="text">The text to generate embedding for</param>
//    /// <param name="cancellationToken">Cancellation token</param>
//    /// <returns>The generated embedding</returns>
//    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(
//        string text,
//        CancellationToken cancellationToken = default)
//    {
//        if (string.IsNullOrWhiteSpace(text))
//            throw new ArgumentException("Text cannot be null or empty", nameof(text));

//        try
//        {
//            var embedding = await _EmbeddingGenerator.GenerateEmbeddingAsync(
//                text,
//                options: null,
//                cancellationToken);
//            return new ReadOnlyMemory<float>(embedding.Vector.ToArray());
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Error generating embedding for text: {ex.Message}");
//            throw;
//        }
//    }

//    /// <summary>
//    /// Get embedding generation statistics
//    /// </summary>
//    /// <param name="chunks">The chunks that have embeddings</param>
//    /// <returns>Statistics about the embeddings</returns>
//    public static EmbeddingStatistics GetEmbeddingStatistics(IList<BookContentChunk> chunks)
//    {
//        var chunksWithEmbeddings = chunks.Where(c => c.TextEmbedding.HasValue).ToList();

//        return new EmbeddingStatistics
//        {
//            TotalChunks = chunks.Count,
//            ChunksWithEmbeddings = chunksWithEmbeddings.Count,
//            EmbeddingDimensions = chunksWithEmbeddings.FirstOrDefault()?.TextEmbedding?.Length ?? 0,
//            AverageTextLength = chunks.Any() ? (int)chunks.Average(c => c.ChunkText.Length) : 0,
//            TotalTextCharacters = chunks.Sum(c => c.ChunkText.Length)
//        };
//    }
//}

///// <summary>
///// Statistics about embedding generation
///// </summary>
//public record EmbeddingStatistics
//{
//    public int TotalChunks { get; init; }
//    public int ChunksWithEmbeddings { get; init; }
//    public int EmbeddingDimensions { get; init; }
//    public int AverageTextLength { get; init; }
//    public int TotalTextCharacters { get; init; }

//    public double EmbeddingCoverage => TotalChunks > 0 ? (double)ChunksWithEmbeddings / TotalChunks * 100 : 0;
//}
