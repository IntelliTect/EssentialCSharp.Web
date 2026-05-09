using EssentialCSharp.Chat.Common.Models;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.VectorData;
using Moq;
using Moq.Language.Flow;
using Npgsql;

namespace EssentialCSharp.Chat.Tests;

public class AISearchServiceTests
{
    private static readonly BookContentChunk _TestChunk = new() { Id = "test-1", ChunkText = "test" };

    private static (AISearchService svc, Mock<VectorStoreCollection<string, BookContentChunk>> collectionMock)
        CreateService()
    {
        var collectionMock = new Mock<VectorStoreCollection<string, BookContentChunk>>();

        var vectorStoreMock = new Mock<VectorStore>();
        vectorStoreMock
            .Setup(vs => vs.GetCollection<string, BookContentChunk>(It.IsAny<string>(), It.IsAny<VectorStoreCollectionDefinition?>()))
            .Returns(collectionMock.Object);

        // IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync is the batch interface method
        // called internally by the single-value extension used in EmbeddingService.GenerateEmbeddingAsync.
        var embGenMock = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        embGenMock
            .Setup(g => g.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[1536])]));

        // NpgsqlDataSource has no default constructor so Moq cannot proxy it.
        // The upload path is not exercised by these tests, so pass null.
        var embeddingService = new EmbeddingService(vectorStoreMock.Object, embGenMock.Object);
        var loggerMock = new Mock<ILogger<AISearchService>>();

        return (new AISearchService(vectorStoreMock.Object, embeddingService, loggerMock.Object), collectionMock);
    }

    private static async IAsyncEnumerable<VectorSearchResult<BookContentChunk>> OneResultStream()
    {
        yield return new VectorSearchResult<BookContentChunk>(_TestChunk, 0.9f);
        await Task.CompletedTask;
    }

    private static ISetup<VectorStoreCollection<string, BookContentChunk>, IAsyncEnumerable<VectorSearchResult<BookContentChunk>>>
        SetupSearch(Mock<VectorStoreCollection<string, BookContentChunk>> mock) =>
            mock.Setup(c => c.SearchAsync(
                It.IsAny<ReadOnlyMemory<float>>(),
                It.IsAny<int>(),
                It.IsAny<VectorSearchOptions<BookContentChunk>?>(),
                It.IsAny<CancellationToken>()));

    [Test]
    public async Task ExecuteVectorSearch_HappyPath_ReturnsResultsWithoutRetry()
    {
        var (svc, collectionMock) = CreateService();
        int callCount = 0;

        SetupSearch(collectionMock).Returns(() => { callCount++; return OneResultStream(); });

        var results = await svc.ExecuteVectorSearch("test query");

        await Assert.That(results.Count).IsEqualTo(1);
        await Assert.That(callCount).IsEqualTo(1);
    }

    [Test]
    public async Task ExecuteVectorSearch_RetriesOnce_WhenFirstAttemptThrows28000()
    {
        var (svc, collectionMock) = CreateService();
        int callCount = 0;

        SetupSearch(collectionMock).Returns(() =>
        {
            callCount++;
            if (callCount == 1)
                throw new PostgresException("auth token expired", "FATAL", "FATAL", "28000");
            return OneResultStream();
        });

        var results = await svc.ExecuteVectorSearch("test query");

        await Assert.That(results.Count).IsEqualTo(1);
        await Assert.That(callCount).IsEqualTo(2);
    }

    [Test]
    public async Task ExecuteVectorSearch_DoesNotRetry_WhenSqlStateIsNot28000()
    {
        var (svc, collectionMock) = CreateService();

        SetupSearch(collectionMock).Returns(() => throw new PostgresException("table not found", "ERROR", "ERROR", "42P01"));

        await Assert.ThrowsAsync<PostgresException>(() => svc.ExecuteVectorSearch("test query"));
    }

    [Test]
    public async Task ExecuteVectorSearch_PropagatesException_WhenBothAttemptsFail28000()
    {
        var (svc, collectionMock) = CreateService();

        SetupSearch(collectionMock).Returns(() => throw new PostgresException("auth failed", "FATAL", "FATAL", "28000"));

        await Assert.ThrowsAsync<PostgresException>(() => svc.ExecuteVectorSearch("test query"));
    }

    [Test]
    public async Task ExecuteVectorSearch_DeduplicatesByHeading_KeepsHighestScoringChunkPerHeading()
    {
        var (svc, collectionMock) = CreateService();

        var chunkA1 = new BookContentChunk { Id = "a1", ChunkText = "text a1", Heading = "Section A" };
        var chunkA2 = new BookContentChunk { Id = "a2", ChunkText = "text a2", Heading = "Section A" };
        var chunkB  = new BookContentChunk { Id = "b1", ChunkText = "text b1", Heading = "Section B" };

        async IAsyncEnumerable<VectorSearchResult<BookContentChunk>> MultiResultStream()
        {
            // a1 scores lower than a2 — dedup should keep a2
            yield return new VectorSearchResult<BookContentChunk>(chunkA1, 0.7f);
            yield return new VectorSearchResult<BookContentChunk>(chunkA2, 0.9f);
            yield return new VectorSearchResult<BookContentChunk>(chunkB, 0.8f);
            await Task.CompletedTask;
        }

        SetupSearch(collectionMock).Returns(MultiResultStream);

        // top defaults to 5, so both distinct headings should appear
        var results = await svc.ExecuteVectorSearch("test query");

        await Assert.That(results.Count).IsEqualTo(2);
        // Highest-scoring chunk for Section A should be a2 (score 0.9), ordered before b (0.8)
        await Assert.That(results[0].Record.Id).IsEqualTo("a2");
        await Assert.That(results[1].Record.Id).IsEqualTo("b1");
    }
}
