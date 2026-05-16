using System.Text.RegularExpressions;
using EssentialCSharp.Chat.Common.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Npgsql;

namespace EssentialCSharp.Chat.Common.Services;

/// <summary>
/// Service for generating embeddings for markdown chunks using Azure OpenAI and uploading
/// them to a PostgreSQL vector store via a staging-then-swap pattern to avoid downtime.
/// Automatically retries on transient Azure OpenAI failures (429 rate limit, 500/503 errors, timeouts)
/// using exponential backoff with jitter.
/// </summary>
public class EmbeddingService(
    VectorStore vectorStore,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IOptions<RetryOptions> retryOptions,
    ILogger<EmbeddingService>? logger = null,
    NpgsqlDataSource? dataSource = null)
{
    public static string CollectionName { get; } = "markdown_chunks";

    /// <summary>
    /// Maximum number of inputs per Azure OpenAI embedding batch call.
    /// </summary>
    private const int EmbeddingBatchSize = 2048;

    private readonly RetryOptions _retryOptions = retryOptions?.Value ?? new RetryOptions();
    private readonly ILogger<EmbeddingService>? _logger = logger;
    private readonly Random _random = new();

    // Only allow simple identifiers: letters, digits, and underscores, starting with a letter or underscore.
    private static readonly Regex _safeIdentifierRegex = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);

    /// <summary>
    /// Initializes the RetryOptions if not provided via dependency injection.
    /// This is useful for scenarios where RetryOptions is not registered in DI.
    /// </summary>
    public EmbeddingService(
        VectorStore vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        NpgsqlDataSource? dataSource = null)
        : this(vectorStore, embeddingGenerator, Options.Create(new RetryOptions()), null, dataSource)
    {
    }

    /// <summary>
    /// Determines whether an exception represents a transient error that should be retried.
    /// </summary>
    private static bool IsTransientError(Exception ex)
    {
        // HttpRequestException can represent various HTTP errors, but we specifically
        // check for 429, 500, 503, and timeout-related exceptions
        if (ex is HttpRequestException httpEx)
        {
            return httpEx.StatusCode is System.Net.HttpStatusCode.TooManyRequests or // 429
                                        System.Net.HttpStatusCode.InternalServerError or // 500
                                        System.Net.HttpStatusCode.ServiceUnavailable; // 503
        }

        // Timeout errors are transient
        if (ex is TaskCanceledException or TimeoutException)
            return true;

        // Check inner exceptions
        if (ex.InnerException != null)
            return IsTransientError(ex.InnerException);

        return false;
    }

    /// <summary>
    /// Extracts the Retry-After delay from an HttpRequestException if present.
    /// Returns null if the header is not present or invalid.
    /// </summary>
    private static TimeSpan? ExtractRetryAfter(Exception ex)
    {
        if (ex is not HttpRequestException httpEx)
            return null;

        // Azure OpenAI may include Retry-After header with delay in seconds
        // This would be accessible via the response, but HttpRequestException
        // doesn't expose headers directly. Log the attempt but rely on
        // exponential backoff as primary mechanism.
        return null;
    }

    /// <summary>
    /// Calculates the delay for the given retry attempt using exponential backoff with jitter.
    /// </summary>
    private TimeSpan CalculateRetryDelay(int attemptNumber)
    {
        // Exponential backoff: baseDelay * (multiplier ^ attemptNumber)
        var delayMs = _retryOptions.BaseDelayMs * 
                     Math.Pow(_retryOptions.BackoffMultiplier, attemptNumber);

        // Add jitter to prevent thundering herd
        var jitterMs = delayMs * _retryOptions.MaxJitterFraction * _random.NextDouble();
        var totalDelayMs = delayMs + jitterMs;

        return TimeSpan.FromMilliseconds(totalDelayMs);
    }

    /// <summary>
    /// Wraps an async operation with retry logic for transient failures.
    /// </summary>
#pragma warning disable CA1848 // Use LoggerMessage delegates - suppressed for simplicity
    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (int attempt = 0; attempt <= _retryOptions.MaxRetries; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (Exception ex) when (IsTransientError(ex) && attempt < _retryOptions.MaxRetries)
            {
                lastException = ex;
                var delay = CalculateRetryDelay(attempt);
                var retryAfter = ExtractRetryAfter(ex);
                var waitTime = retryAfter ?? delay;

                _logger?.LogWarning(
                    "Transient error during {OperationName} (attempt {Attempt}/{MaxRetries}). " +
                    "Will retry after {DelayMs}ms. Error: {ErrorMessage}",
                    operationName, attempt + 1, _retryOptions.MaxRetries + 1,
                    (int)waitTime.TotalMilliseconds, ex.Message);

                await Task.Delay(waitTime, cancellationToken);
            }
            catch (Exception ex)
            {
                // Permanent error or exceeded max retries
                _logger?.LogError(ex,
                    "Operation {OperationName} failed with {ExceptionType}: {ErrorMessage}",
                    operationName, ex.GetType().Name, ex.Message);
                throw;
            }
        }

        // Max retries exceeded with transient errors
        _logger?.LogError(lastException,
            "Operation {OperationName} failed after {MaxRetries} retries. Last error: {ErrorMessage}",
            operationName, _retryOptions.MaxRetries, lastException?.Message);

        throw new InvalidOperationException(
            $"Operation {operationName} failed after {_retryOptions.MaxRetries} retry attempts. " +
            $"Last error: {lastException?.Message}", lastException);
    }
#pragma warning restore CA1848

    /// <summary>
    /// Generate an embedding for the given text.
    /// Automatically retries on transient Azure OpenAI failures.
    /// </summary>
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var embedding = await ExecuteWithRetryAsync(
            async ct => await embeddingGenerator.GenerateAsync(text, cancellationToken: ct),
            $"GenerateEmbedding",
            cancellationToken);
        return embedding.Vector;
    }

    /// <summary>
    /// Generate embeddings for all chunks in batches and upload them to the vector store
    /// using a staging-then-atomic-swap pattern so the live collection stays queryable
    /// throughout the rebuild.
    ///
    /// Steps:
    ///   1. Create a staging collection ({collectionName}_staging).
    ///   2. For each batch of <see cref="EmbeddingBatchSize"/> chunks: embed the batch
    ///      and immediately upsert it into staging, keeping peak memory bounded.
    ///   3. Atomically swap tables in a single transaction using two SQL RENAME operations
    ///      (live → old, staging → live). PostgreSQL ALTER TABLE acquires
    ///      AccessExclusiveLock automatically; no explicit LOCK TABLE is needed. The
    ///      transaction ensures no reader sees an intermediate state.
    ///   4. Drop the old live backup table with DROP TABLE.
    ///
    /// If an error occurs before the swap, only the staging table is affected — the live
    /// collection is untouched.
    /// </summary>
    public async Task GenerateBookContentEmbeddingsAndUploadToVectorStore(
        IEnumerable<BookContentChunk> bookContents,
        CancellationToken cancellationToken,
        string? collectionName = null)
    {
        collectionName ??= CollectionName;

        if (dataSource is null)
            throw new InvalidOperationException(
                $"{nameof(NpgsqlDataSource)} must be provided to upload embeddings. Ensure it is registered in DI.");

        if (!_safeIdentifierRegex.IsMatch(collectionName))
            throw new ArgumentException(
                $"Collection name '{collectionName}' contains unsafe characters. Use only letters, digits, and underscores.",
                nameof(collectionName));

        string stagingName = $"{collectionName}_staging";
        string oldName = $"{collectionName}_old";

        // ── Step 1: Prepare staging collection ────────────────────────────────────────
        var staging = vectorStore.GetCollection<string, BookContentChunk>(stagingName);
        await staging.EnsureCollectionDeletedAsync(cancellationToken);
        await staging.EnsureCollectionExistsAsync(cancellationToken);

        // ── Step 2 & 3: Batch-embed and immediately upsert each batch ─────────────────
        // Azure OpenAI supports at most EmbeddingBatchSize inputs per GenerateAsync call.
        // bookContents is streamed in fixed-size batches without full upfront materialization,
        // keeping peak memory bounded to one batch of chunk objects and their embeddings at a time.
        // The staging-swap (Step 3) is safe because it only runs after all batches have
        // been successfully upserted.
        var buffer = new List<BookContentChunk>(EmbeddingBatchSize);
        int totalCount = 0;

        async Task EmbedAndUpsertBatchAsync()
        {
            var batchEmbeddings = await ExecuteWithRetryAsync(
                async ct => await embeddingGenerator.GenerateAsync(
                    buffer.Select(c => c.ChunkText), cancellationToken: ct),
                $"GenerateBatchEmbeddings(size={buffer.Count})",
                cancellationToken);

            if (batchEmbeddings.Count != buffer.Count)
                throw new InvalidOperationException(
                    $"Embedding count mismatch: expected {buffer.Count}, got {batchEmbeddings.Count}.");

            for (int i = 0; i < buffer.Count; i++)
                buffer[i].TextEmbedding = batchEmbeddings[i].Vector;

            await staging.UpsertAsync(buffer, cancellationToken);
            totalCount += buffer.Count;
            buffer.Clear();
        }

        try
        {
            foreach (var chunk in bookContents)
            {
                buffer.Add(chunk);
                if (buffer.Count == EmbeddingBatchSize)
                    await EmbedAndUpsertBatchAsync();
            }

            if (buffer.Count > 0)
                await EmbedAndUpsertBatchAsync();

            Console.WriteLine($"Uploaded {totalCount} chunks to staging collection '{stagingName}'.");
        }
        catch
        {
            // Best-effort cleanup: drop the partially-populated staging table so the
            // next run starts clean. Do not let this secondary failure mask the original.
            try
            {
                await staging.EnsureCollectionDeletedAsync(cancellationToken);
            }
            catch (Exception cleanupEx) when (cleanupEx is not OperationCanceledException)
            {
                Console.Error.WriteLine($"Warning: failed to clean up staging collection '{stagingName}' after upsert failure: {cleanupEx.Message}");
            }
            throw;
        }

        // ── Step 3: Atomic swap — staging → live ──────────────────────────────────────
        // Two ALTER TABLE RENAME operations in one transaction (live → old, staging → live).
        // Each RENAME auto-acquires AccessExclusiveLock on its table; the transaction
        // guarantees both renames are visible atomically to other sessions.
        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;

            // Drop any leftover backup from a previous run
            cmd.CommandText = $"DROP TABLE IF EXISTS \"{oldName}\"";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // Rename live → old. IF EXISTS is a no-op on first run when no live table exists.
            cmd.CommandText = $"ALTER TABLE IF EXISTS \"{collectionName}\" RENAME TO \"{oldName}\"";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // Rename staging → live
            cmd.CommandText = $"ALTER TABLE \"{stagingName}\" RENAME TO \"{collectionName}\"";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        Console.WriteLine($"Swapped '{stagingName}' → '{collectionName}' atomically.");

        // ── Step 4: Drop the old backup ───────────────────────────────────────────────
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"DROP TABLE IF EXISTS \"{oldName}\"";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        Console.WriteLine($"Successfully generated embeddings and uploaded {totalCount} chunks to collection '{collectionName}'.");
    }
}
