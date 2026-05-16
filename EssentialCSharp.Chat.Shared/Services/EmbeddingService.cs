using System.Text.RegularExpressions;
using EssentialCSharp.Chat.Common.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Npgsql;
using System.ClientModel;
using System.ClientModel.Primitives;
using System.Globalization;

namespace EssentialCSharp.Chat.Common.Services;

/// <summary>
/// Service for generating embeddings for markdown chunks using Azure OpenAI and uploading
/// them to a PostgreSQL vector store via a staging-then-swap pattern to avoid downtime.
/// Automatically retries on transient Azure OpenAI failures (429 rate limit, 500/503 errors, timeouts)
/// using exponential backoff with jitter.
/// </summary>
public partial class EmbeddingService(
    VectorStore vectorStore,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    IOptions<EmbeddingRetryOptions> retryOptions,
    ILogger<EmbeddingService>? logger = null,
    NpgsqlDataSource? dataSource = null)
{
    public static string CollectionName { get; } = "markdown_chunks";

    /// <summary>
    /// Maximum number of inputs per Azure OpenAI embedding batch call.
    /// </summary>
    private const int EmbeddingBatchSize = 2048;

    private readonly EmbeddingRetryOptions _retryOptions = ValidateRetryOptions(retryOptions?.Value ?? new EmbeddingRetryOptions());
    private readonly ILogger<EmbeddingService>? _logger = logger;
    private static readonly SemaphoreSlim _embeddingRequestLock = new(1, 1);
    private DateTimeOffset _lastEmbeddingRequestStartedUtc = DateTimeOffset.MinValue;

    // Only allow simple identifiers: letters, digits, and underscores, starting with a letter or underscore.
    private static readonly Regex _safeIdentifierRegex = new(@"^[a-zA-Z_][a-zA-Z0-9_]*$", RegexOptions.Compiled);
    private static readonly Regex _retryAfterSecondsRegex = new(@"retry\s+after\s+(?<seconds>\d+)\s+seconds?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static EmbeddingRetryOptions ValidateRetryOptions(EmbeddingRetryOptions options)
    {
        options.Validate();
        return options;
    }

    /// <summary>
    /// Initializes the embedding retry options if not provided via dependency injection.
    /// This is useful for scenarios where embedding retry options are not registered in DI.
    /// </summary>
    public EmbeddingService(
        VectorStore vectorStore,
        IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
        NpgsqlDataSource? dataSource = null)
        : this(vectorStore, embeddingGenerator, Options.Create(new EmbeddingRetryOptions()), null, dataSource)
    {
    }

    /// <summary>
    /// Determines whether an exception represents a transient error that should be retried.
    /// </summary>
    private static bool IsTransientError(Exception ex)
    {
        if (ex is ClientResultException clientResultEx)
            return IsTransientStatusCode(clientResultEx.Status);

        if (ex is HttpRequestException httpEx)
        {
            return httpEx.StatusCode is System.Net.HttpStatusCode.TooManyRequests or
                                        System.Net.HttpStatusCode.InternalServerError or
                                        System.Net.HttpStatusCode.ServiceUnavailable or
                                        System.Net.HttpStatusCode.GatewayTimeout or
                                        System.Net.HttpStatusCode.RequestTimeout;
        }

        // Timeout errors are transient
        if (ex is TaskCanceledException or TimeoutException)
            return true;

        // Check inner exceptions
        if (ex.InnerException != null)
            return IsTransientError(ex.InnerException);

        return false;
    }

    private static bool IsTransientStatusCode(int statusCode) =>
        statusCode is 408 or 429 or 500 or 502 or 503 or 504;

    private static int? TryGetStatusCode(Exception ex)
    {
        if (ex is ClientResultException clientResultException)
            return clientResultException.Status;

        if (ex is HttpRequestException httpRequestException && httpRequestException.StatusCode is not null)
            return (int)httpRequestException.StatusCode.Value;

        return ex.InnerException is null ? null : TryGetStatusCode(ex.InnerException);
    }

    private static bool IsRateLimitError(Exception ex) =>
        TryGetStatusCode(ex) == 429
        || ex.Message.Contains("RateLimitReached", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts the Retry-After delay from known exception types if present.
    /// Returns null if the header is not present or invalid.
    /// </summary>
    private static TimeSpan? ExtractRetryAfter(Exception ex)
    {
        if (ex is ClientResultException clientResultException)
        {
            var rawResponse = clientResultException.GetRawResponse();
            if (TryGetHeaderValue(rawResponse, "x-ms-retry-after-ms", out var msHeaderValue)
                && TryParseMilliseconds(msHeaderValue, out var msRetryAfter))
            {
                return msRetryAfter;
            }

            if (TryGetHeaderValue(rawResponse, "retry-after-ms", out var retryAfterMsHeaderValue)
                && TryParseMilliseconds(retryAfterMsHeaderValue, out var retryAfterMs))
            {
                return retryAfterMs;
            }

            if (TryGetHeaderValue(rawResponse, "retry-after", out var headerValue)
                && TryParseRetryAfterValue(headerValue, out var retryAfter))
            {
                return retryAfter;
            }
        }

        if (TryParseRetryAfterValue(ex.Message, out var messageRetryAfter))
            return messageRetryAfter;

        return ex.InnerException is null ? null : ExtractRetryAfter(ex.InnerException);
    }

    private static bool TryGetHeaderValue(PipelineResponse? response, string headerName, out string? headerValue)
    {
        headerValue = null;
        if (response is null)
            return false;

        if (response.Headers.TryGetValue(headerName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            headerValue = value;
            return true;
        }

        return false;
    }

    private static bool TryParseMilliseconds(string? value, out TimeSpan retryAfter)
    {
        retryAfter = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var msValue) && msValue >= 0)
        {
            retryAfter = TimeSpan.FromMilliseconds(msValue);
            return true;
        }

        return false;
    }

    private static bool TryParseRetryAfterValue(string? headerValue, out TimeSpan retryAfter)
    {
        retryAfter = default;
        if (string.IsNullOrWhiteSpace(headerValue))
            return false;

        if (int.TryParse(headerValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
        {
            retryAfter = TimeSpan.FromSeconds(seconds);
            return true;
        }

        if (DateTimeOffset.TryParse(headerValue, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var retryAt))
        {
            var delay = retryAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
            {
                retryAfter = delay;
                return true;
            }
        }

        var secondsMatch = _retryAfterSecondsRegex.Match(headerValue);
        if (secondsMatch.Success
            && int.TryParse(secondsMatch.Groups["seconds"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var extractedSeconds)
            && extractedSeconds >= 0)
        {
            retryAfter = TimeSpan.FromSeconds(extractedSeconds);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Calculates the delay for the given retry attempt using exponential backoff with jitter.
    /// </summary>
    private TimeSpan CalculateRetryDelay(int attemptNumber)
    {
        // Exponential backoff: baseDelay * (multiplier ^ attemptNumber), capped to avoid overflow/unbounded delays.
        var rawDelayMs = _retryOptions.BaseDelayMs *
                         Math.Pow(_retryOptions.BackoffMultiplier, attemptNumber);
        var cappedDelayMs = Math.Min(_retryOptions.MaxDelayMs, rawDelayMs);

        // Add jitter to prevent thundering herd
        var jitterMs = cappedDelayMs * _retryOptions.MaxJitterFraction * Random.Shared.NextDouble();
        var totalDelayMs = cappedDelayMs + jitterMs;

        return TimeSpan.FromMilliseconds(totalDelayMs);
    }

    private TimeSpan ClampRetryDelay(TimeSpan delay) =>
        delay > TimeSpan.FromMilliseconds(_retryOptions.MaxDelayMs)
            ? TimeSpan.FromMilliseconds(_retryOptions.MaxDelayMs)
            : delay;

    private async Task<T> ExecuteEmbeddingRequestWithPacingAsync<T>(
        Func<CancellationToken, Task<T>> embeddingRequest,
        CancellationToken cancellationToken)
    {
        await _embeddingRequestLock.WaitAsync(cancellationToken);
        try
        {
            var minimumSpacing = TimeSpan.FromMilliseconds(_retryOptions.MinInterRequestDelayMs);
            if (minimumSpacing > TimeSpan.Zero && _lastEmbeddingRequestStartedUtc != DateTimeOffset.MinValue)
            {
                var elapsed = DateTimeOffset.UtcNow - _lastEmbeddingRequestStartedUtc;
                var remainingDelay = minimumSpacing - elapsed;
                if (remainingDelay > TimeSpan.Zero)
                    await Task.Delay(remainingDelay, cancellationToken);
            }

            _lastEmbeddingRequestStartedUtc = DateTimeOffset.UtcNow;
            return await embeddingRequest(cancellationToken);
        }
        finally
        {
            _embeddingRequestLock.Release();
        }
    }

    /// <summary>
    /// Wraps an async operation with retry logic for transient failures.
    /// </summary>
    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt <= _retryOptions.MaxRetries; attempt++)
        {
            try
            {
                return await operation(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientError(ex) && attempt < _retryOptions.MaxRetries)
            {
                var delay = CalculateRetryDelay(attempt);
                var retryAfter = ExtractRetryAfter(ex);
                var waitTime = retryAfter.HasValue ? ClampRetryDelay(retryAfter.Value) : delay;
                var statusCode = TryGetStatusCode(ex);

                if (_logger is not null)
                {
                    LogRetryingTransientEmbeddingFailure(
                        _logger,
                        operationName,
                        attempt + 1,
                        _retryOptions.MaxRetries + 1,
                        (int)waitTime.TotalMilliseconds,
                        ex.GetType().Name,
                        ex.Message,
                        statusCode);
                }

                await Task.Delay(waitTime, cancellationToken);
            }
            catch (Exception ex) when (IsTransientError(ex))
            {
                if (_logger is not null)
                {
                    LogEmbeddingRetryAttemptsExhausted(
                        _logger,
                        ex,
                        operationName,
                        _retryOptions.MaxRetries + 1,
                        ex.Message,
                        TryGetStatusCode(ex));
                }

                throw;
            }
            catch (Exception ex)
            {
                if (_logger is not null)
                    LogEmbeddingOperationFailed(_logger, ex, operationName, ex.GetType().Name, ex.Message, TryGetStatusCode(ex));
                throw;
            }
        }
        throw new InvalidOperationException($"Operation {operationName} ended without result unexpectedly.");
    }

    /// <summary>
    /// Generate an embedding for the given text.
    /// Automatically retries on transient Azure OpenAI failures.
    /// </summary>
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var embedding = await ExecuteWithRetryAsync(
            async ct => await ExecuteEmbeddingRequestWithPacingAsync(
                async pacingCt => await embeddingGenerator.GenerateAsync(text, cancellationToken: pacingCt),
                ct),
            "GenerateEmbedding",
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
        // The effective request size starts at min(EmbeddingBatchSize, MaxEmbeddingBatchSize)
        // and adaptively downshifts on 429 throttling responses.
        // bookContents is streamed in batches without full upfront materialization,
        // keeping peak memory bounded to one batch (or adaptive split) at a time.
        // The staging-swap (Step 3) is safe because it only runs after all batches have
        // been successfully upserted.
        var configuredMaxBatchSize = Math.Clamp(_retryOptions.MaxEmbeddingBatchSize, 1, EmbeddingBatchSize);
        var adaptiveBatchSize = configuredMaxBatchSize;
        var buffer = new List<BookContentChunk>(configuredMaxBatchSize);
        var knownTotalChunks = bookContents.TryGetNonEnumeratedCount(out var totalChunkCount) ? totalChunkCount : (int?)null;
        var nextProgressPercentToLog = 10;
        var nextProgressChunkCountToLog = 500;
        int totalCount = 0;

        if (_logger is not null)
        {
            LogEmbeddingRebuildStarted(
                _logger,
                knownTotalChunks,
                configuredMaxBatchSize,
                _retryOptions.MinInterRequestDelayMs);
        }

        void LogProgressIfNeeded()
        {
            if (_logger is null)
                return;

            if (knownTotalChunks is > 0)
            {
                while (nextProgressPercentToLog <= 100
                    && totalCount * 100 >= knownTotalChunks.Value * nextProgressPercentToLog)
                {
                    LogEmbeddingProgressPercent(_logger, totalCount, knownTotalChunks.Value, nextProgressPercentToLog, adaptiveBatchSize);
                    nextProgressPercentToLog += 10;
                }
            }
            else if (totalCount >= nextProgressChunkCountToLog)
            {
                LogEmbeddingProgressCount(_logger, totalCount, adaptiveBatchSize);
                nextProgressChunkCountToLog += 500;
            }
        }

        async Task EmbedAndUpsertExactBatchAsync(IReadOnlyList<BookContentChunk> batch)
        {
            var batchEmbeddings = await ExecuteWithRetryAsync(
                async ct => await ExecuteEmbeddingRequestWithPacingAsync(
                    async pacingCt => await embeddingGenerator.GenerateAsync(
                        batch.Select(c => c.ChunkText), cancellationToken: pacingCt),
                    ct),
                $"GenerateBatchEmbeddings(size={batch.Count})",
                cancellationToken);

            if (batchEmbeddings.Count != batch.Count)
                throw new InvalidOperationException(
                    $"Embedding count mismatch: expected {batch.Count}, got {batchEmbeddings.Count}.");

            for (int i = 0; i < batch.Count; i++)
                batch[i].TextEmbedding = batchEmbeddings[i].Vector;

            await staging.UpsertAsync(batch, cancellationToken);
            totalCount += batch.Count;
            LogProgressIfNeeded();
        }

        async Task EmbedAndUpsertBatchAdaptiveAsync(IReadOnlyList<BookContentChunk> batch)
        {
            try
            {
                await EmbedAndUpsertExactBatchAsync(batch);
            }
            catch (Exception ex) when (IsRateLimitError(ex) && batch.Count > 1)
            {
                var splitSize = Math.Max(1, batch.Count / 2);
                if (adaptiveBatchSize > splitSize)
                {
                    var previousAdaptiveBatchSize = adaptiveBatchSize;
                    adaptiveBatchSize = splitSize;
                    if (_logger is not null)
                    {
                        LogEmbeddingBatchDownshift(_logger, previousAdaptiveBatchSize, adaptiveBatchSize, _retryOptions.MaxRetries + 1);
                    }
                }

                var first = batch.Take(splitSize).ToArray();
                var second = batch.Skip(splitSize).ToArray();
                await EmbedAndUpsertBatchAdaptiveAsync(first);
                await EmbedAndUpsertBatchAdaptiveAsync(second);
            }
            catch (Exception ex) when (IsRateLimitError(ex) && batch.Count == 1)
            {
                throw new InvalidOperationException(
                    $"Embedding request failed with repeated 429 rate limits even at batch size 1 after {_retryOptions.MaxRetries + 1} attempts.",
                    ex);
            }
        }

        try
        {
            foreach (var chunk in bookContents)
            {
                buffer.Add(chunk);
                if (buffer.Count >= adaptiveBatchSize)
                {
                    var batchToProcess = buffer.ToArray();
                    buffer.Clear();
                    await EmbedAndUpsertBatchAdaptiveAsync(batchToProcess);
                }
            }

            if (buffer.Count > 0)
            {
                var batchToProcess = buffer.ToArray();
                buffer.Clear();
                await EmbedAndUpsertBatchAdaptiveAsync(batchToProcess);
            }

            Console.WriteLine($"Uploaded {totalCount} chunks to staging collection '{stagingName}'.");
        }
        catch
        {
            // Best-effort cleanup: drop the partially-populated staging table so the
            // next run starts clean. Do not let this secondary failure mask the original.
            try
            {
                await staging.EnsureCollectionDeletedAsync(CancellationToken.None);
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

    [LoggerMessage(
        EventId = 12001,
        Level = LogLevel.Warning,
        Message = "Transient embedding failure during {OperationName}. Attempt {Attempt}/{MaxAttempts}. Retrying in {DelayMs} ms. Exception={ExceptionType} StatusCode={StatusCode}. Message={ErrorMessage}")]
    private static partial void LogRetryingTransientEmbeddingFailure(
        ILogger logger,
        string operationName,
        int attempt,
        int maxAttempts,
        int delayMs,
        string exceptionType,
        string errorMessage,
        int? statusCode);

    [LoggerMessage(
        EventId = 12002,
        Level = LogLevel.Error,
        Message = "Embedding operation failed without retry: {OperationName}. Exception={ExceptionType} StatusCode={StatusCode}. Message={ErrorMessage}")]
    private static partial void LogEmbeddingOperationFailed(
        ILogger logger,
        Exception exception,
        string operationName,
        string exceptionType,
        string errorMessage,
        int? statusCode);

    [LoggerMessage(
        EventId = 12003,
        Level = LogLevel.Error,
        Message = "Embedding retries exhausted for {OperationName} after {AttemptCount} attempts. StatusCode={StatusCode}. LastError={LastError}")]
    private static partial void LogEmbeddingRetryAttemptsExhausted(
        ILogger logger,
        Exception? exception,
        string operationName,
        int attemptCount,
        string lastError,
        int? statusCode);

    [LoggerMessage(
        EventId = 12004,
        Level = LogLevel.Warning,
        Message = "Embedding batch downshift triggered after throttling. PreviousBatchSize={PreviousBatchSize}, NewBatchSize={NewBatchSize}, RetryAttemptsPerRequest={RetryAttemptsPerRequest}")]
    private static partial void LogEmbeddingBatchDownshift(
        ILogger logger,
        int previousBatchSize,
        int newBatchSize,
        int retryAttemptsPerRequest);

    [LoggerMessage(
        EventId = 12005,
        Level = LogLevel.Information,
        Message = "Embedding rebuild started. TotalChunks={TotalChunks}, InitialBatchSize={InitialBatchSize}, MinInterRequestDelayMs={MinInterRequestDelayMs}")]
    private static partial void LogEmbeddingRebuildStarted(
        ILogger logger,
        int? totalChunks,
        int initialBatchSize,
        int minInterRequestDelayMs);

    [LoggerMessage(
        EventId = 12006,
        Level = LogLevel.Information,
        Message = "Embedding progress: {ProcessedChunks}/{TotalChunks} chunks ({ProgressPercent}%). CurrentAdaptiveBatchSize={AdaptiveBatchSize}")]
    private static partial void LogEmbeddingProgressPercent(
        ILogger logger,
        int processedChunks,
        int totalChunks,
        int progressPercent,
        int adaptiveBatchSize);

    [LoggerMessage(
        EventId = 12007,
        Level = LogLevel.Information,
        Message = "Embedding progress: {ProcessedChunks} chunks processed. CurrentAdaptiveBatchSize={AdaptiveBatchSize}")]
    private static partial void LogEmbeddingProgressCount(
        ILogger logger,
        int processedChunks,
        int adaptiveBatchSize);
}
