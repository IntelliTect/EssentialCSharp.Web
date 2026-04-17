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
}
