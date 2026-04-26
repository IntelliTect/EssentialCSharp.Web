using EssentialCSharp.Chat.Common.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.VectorData;
using Npgsql;

namespace EssentialCSharp.Chat.Common.Services;

/// <summary>
/// Service for generating embeddings for markdown chunks using Azure OpenAI and uploading
/// them to a PostgreSQL vector store via a staging-then-swap pattern to avoid downtime.
/// </summary>
public class EmbeddingService(
    VectorStore vectorStore,
    IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    NpgsqlDataSource? dataSource = null)
{
    public static string CollectionName { get; } = "markdown_chunks";

    /// <summary>
    /// Generate an embedding for the given text.
    /// </summary>
    public async Task<ReadOnlyMemory<float>> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var embedding = await embeddingGenerator.GenerateAsync(text, cancellationToken: cancellationToken);
        return embedding.Vector;
    }

    /// <summary>
    /// Generate embeddings for all chunks in a single batch call and upload them to the vector
    /// store using a staging-then-atomic-swap pattern so the live collection stays queryable
    /// throughout the rebuild.
    ///
    /// Steps:
    ///   1. Create a staging collection ({collectionName}_staging).
    ///   2. Embed all chunks in one batch API call (Azure OpenAI supports up to 2048 inputs).
    ///   3. Batch-upsert all chunks into staging.
    ///   4. Atomically swap staging → live via three SQL RENAMEs in a single transaction.
    ///      PostgreSQL ALTER TABLE acquires AccessExclusiveLock automatically; no explicit
    ///      LOCK TABLE is needed. The transaction ensures no reader sees an intermediate state.
    ///   5. Drop the old live backup table.
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
        string stagingName = $"{collectionName}_staging";
        string oldName = $"{collectionName}_old";

        if (dataSource is null)
            throw new InvalidOperationException("NpgsqlDataSource is required for the staging swap. Ensure it is registered in DI.");

        // ── Step 1: Prepare staging collection ────────────────────────────────────────
        var staging = vectorStore.GetCollection<string, BookContentChunk>(stagingName);
        await staging.EnsureCollectionDeletedAsync(cancellationToken);
        await staging.EnsureCollectionExistsAsync(cancellationToken);

        // ── Step 2: Batch-embed all chunks in a single API call ───────────────────────
        // IEmbeddingGenerator.GenerateAsync natively accepts IEnumerable<string>.
        // The single-string overload used previously is a convenience extension method
        // that wraps one item and calls this same method.
        var chunkList = bookContents.ToList();
        var texts = chunkList.Select(c => c.ChunkText).ToList();

        GeneratedEmbeddings<Embedding<float>> embeddings =
            await embeddingGenerator.GenerateAsync(texts, cancellationToken: cancellationToken);

        if (embeddings.Count != chunkList.Count)
            throw new InvalidOperationException(
                $"Embedding count mismatch: expected {chunkList.Count}, got {embeddings.Count}.");

        for (int i = 0; i < chunkList.Count; i++)
        {
            chunkList[i].TextEmbedding = embeddings[i].Vector;
        }

        // ── Step 3: Batch-upsert all chunks into staging ──────────────────────────────
        await staging.UpsertAsync(chunkList, cancellationToken);
        Console.WriteLine($"Uploaded {chunkList.Count} chunks to staging collection '{stagingName}'.");

        // ── Step 4: Atomic swap — staging → live ──────────────────────────────────────
        // Three ALTER TABLE RENAME statements in one transaction.
        // Each RENAME auto-acquires AccessExclusiveLock on its table; the transaction
        // guarantees all three renames are visible atomically to other sessions.
        await using var conn = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;

            // Drop any leftover backup from a previous run
            cmd.CommandText = $"DROP TABLE IF EXISTS \"{oldName}\"";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // Rename live → old. IF EXISTS is a no-op on first run when no live table exists.
            // Using ALTER TABLE IF EXISTS avoids PL/pgSQL string interpolation entirely.
            cmd.CommandText = $"ALTER TABLE IF EXISTS \"{collectionName}\" RENAME TO \"{oldName}\"";
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            // Rename staging → live
            cmd.CommandText = $"ALTER TABLE \"{stagingName}\" RENAME TO \"{collectionName}\"";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
        Console.WriteLine($"Swapped '{stagingName}' → '{collectionName}' atomically.");

        // ── Step 5: Drop the old backup ───────────────────────────────────────────────
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"DROP TABLE IF EXISTS \"{oldName}\"";
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        Console.WriteLine($"Successfully generated embeddings and uploaded {chunkList.Count} chunks to collection '{collectionName}'.");
    }
}
