namespace EssentialCSharp.Chat.Common.Services;

///// <summary>
///// Service for storing and retrieving markdown chunks in PostgreSQL with pgvector
///// Following Microsoft best practices for Semantic Kernel Vector Store
///// </summary>
//public class VectorStoreService
//{
//    private readonly PostgresVectorStore _vectorStore;
//    private readonly string _collectionName;

//    public VectorStoreService(string connectionString, string collectionName = "markdown_chunks")
//    {
//        if (string.IsNullOrWhiteSpace(connectionString))
//            throw new ArgumentException("Connection string cannot be null or empty", nameof(connectionString));

//        _collectionName = collectionName;

//        // Create PostgreSQL vector store using the connection string
//        _vectorStore = new PostgresVectorStore(connectionString);
//    }

//    public VectorStoreService(PostgresVectorStore vectorStore, string collectionName = "markdown_chunks")
//    {
//        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
//        _collectionName = collectionName;
//    }

//    /// <summary>
//    /// Initialize the vector store collection
//    /// </summary>
//    /// <param name="cancellationToken">Cancellation token</param>
//    public async Task InitializeAsync(CancellationToken cancellationToken = default)
//    {
//        Console.WriteLine($"Initializing vector store collection '{_collectionName}'...");

//        try
//        {
//            var collection = GetCollection();

//            // Create the collection if it doesn't exist
//            await collection.CreateCollectionIfNotExistsAsync(cancellationToken);

//            Console.WriteLine($"✅ Vector store collection '{_collectionName}' is ready");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Error initializing vector store: {ex.Message}");
//            throw;
//        }
//    }

//    /// <summary>
//    /// Store markdown chunks in the vector store
//    /// </summary>
//    /// <param name="chunks">The chunks to store</param>
//    /// <param name="cancellationToken">Cancellation token</param>
//    public async Task StoreChunksAsync(IList<BookContentChunk> chunks, CancellationToken cancellationToken = default)
//    {
//        if (!chunks.Any())
//        {
//            Console.WriteLine("No chunks to store");
//            return;
//        }

//        Console.WriteLine($"Storing {chunks.Count} chunks in vector store...");

//        try
//        {
//            var collection = GetCollection();

//            // Store chunks in batches to avoid overwhelming the database
//            const int batchSize = 100;
//            int stored = 0;

//            for (int i = 0; i < chunks.Count; i += batchSize)
//            {
//                var batch = chunks.Skip(i).Take(batchSize).ToList();

//                // Upsert chunks (insert or update if exists)
//                await collection.UpsertBatchAsync(batch, cancellationToken);

//                stored += batch.Count;
//                Console.WriteLine($"   Stored {stored}/{chunks.Count} chunks");
//            }

//            Console.WriteLine($"✅ Successfully stored {chunks.Count} chunks in vector store");
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Error storing chunks: {ex.Message}");
//            throw;
//        }
//    }

//    /// <summary>
//    /// Search for similar chunks using vector similarity
//    /// </summary>
//    /// <param name="queryEmbedding">The query embedding to search with</param>
//    /// <param name="limit">Maximum number of results to return</param>
//    /// <param name="minRelevanceScore">Minimum relevance score (0.0 to 1.0)</param>
//    /// <param name="cancellationToken">Cancellation token</param>
//    /// <returns>Similar chunks ordered by relevance</returns>
//    public async Task<IList<VectorSearchResult<BookContentChunk>>> SearchSimilarChunksAsync(
//        ReadOnlyMemory<float> queryEmbedding,
//        int limit = 10,
//        double minRelevanceScore = 0.0,
//        CancellationToken cancellationToken = default)
//    {
//        Console.WriteLine($"Searching for similar chunks (limit: {limit}, min score: {minRelevanceScore:F2})...");

//        try
//        {
//            var collection = GetCollection();

//            // Perform vector search
//            var searchResults = await collection.VectorizedSearchAsync(
//                queryEmbedding,
//                new()
//                {
//                    Top = limit,
//                    Filter = null // Could add filters for chapter, file, etc.
//                },
//                cancellationToken);

//            var results = await searchResults
//                .Where(r => r.Score >= minRelevanceScore)
//                .ToListAsync(cancellationToken);

//            Console.WriteLine($"✅ Found {results.Count} similar chunks");

//            return results;
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Error searching chunks: {ex.Message}");
//            throw;
//        }
//    }

//    /// <summary>
//    /// Get chunks by file name
//    /// </summary>
//    /// <param name="fileName">The file name to search for</param>
//    /// <param name="cancellationToken">Cancellation token</param>
//    /// <returns>Chunks from the specified file</returns>
//    public async Task<IList<BookContentChunk>> GetChunksByFileAsync(
//    string fileName,
//    CancellationToken cancellationToken = default)
//    {
//        try
//        {
//            var collection = GetCollection();

//            // Use VectorSearchOptions.Filter instead of VectorSearchFilter
//            var options = new VectorSearchOptions
//            {
//                Filter = new VectorSearchFilter().EqualTo(nameof(BookContentChunk.FileName), fileName),
//                Top = 1000 // Large number to get all chunks
//            };

//            var results = await collection.VectorizedSearchAsync(
//                new ReadOnlyMemory<float>(new float[1536]), // Dummy vector, we're filtering not searching
//                options,
//                cancellationToken);

//            var chunks = await results.Select(r => r.Record).ToListAsync(cancellationToken);

//            return chunks;
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Error getting chunks by file: {ex.Message}");
//            throw;
//        }
//    }

//    /// <summary>
//    /// Get collection statistics
//    /// </summary>
//    /// <param name="cancellationToken">Cancellation token</param>
//    /// <returns>Statistics about the collection</returns>
//    public async Task<VectorStoreStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
//    {
//        try
//        {
//            var collection = GetCollection();

//            // Get all chunks to calculate statistics
//            var searchResults = await collection.VectorizedSearchAsync(
//                new ReadOnlyMemory<float>(new float[1536]), // Dummy vector
//                new() { Top = 10000 }, // Large number to get all
//                cancellationToken);

//            var allChunks = await searchResults.Select(r => r.Record).ToListAsync(cancellationToken);

//            var fileGroups = allChunks.GroupBy(c => c.FileName).ToList();

//            return new VectorStoreStatistics
//            {
//                TotalChunks = allChunks.Count,
//                TotalFiles = fileGroups.Count,
//                AverageChunksPerFile = fileGroups.Any() ? fileGroups.Average(g => g.Count()) : 0,
//                ChunksWithEmbeddings = allChunks.Count(c => c.TextEmbedding.HasValue),
//                TotalTokens = allChunks.Sum(c => c.TokenCount)
//            };
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"❌ Error getting statistics: {ex.Message}");
//            throw;
//        }
//    }

//    private VectorStoreCollection<string, BookContentChunk> GetCollection()
//    {
//        return _vectorStore.GetCollection<string, BookContentChunk>(_collectionName);
//    }

//    public void Dispose()
//    {
//        _vectorStore?.Dispose();
//    }
//}

///// <summary>
///// Statistics about the vector store
///// </summary>
//public record VectorStoreStatistics
//{
//    public int TotalChunks { get; init; }
//    public int TotalFiles { get; init; }
//    public double AverageChunksPerFile { get; init; }
//    public int ChunksWithEmbeddings { get; init; }
//    public int TotalTokens { get; init; }

//    public double EmbeddingCoverage => TotalChunks > 0 ? (double)ChunksWithEmbeddings / TotalChunks * 100 : 0;
//}
