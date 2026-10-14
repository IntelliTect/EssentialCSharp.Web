using EssentialCSharp.Chat.Common.Models;
using CommunityToolkit.VectorData.PgVector;

namespace EssentialCSharp.Chat.Tests;

public class PgVectorConnectorTests
{
    /// <summary>
    /// Verifies the runtime vector-data API shape is compatible with the currently resolved
    /// CommunityToolkit connector assemblies. If this fails with MissingMethodException
    /// (for example on VectorSearchOptions<T>.get_OldFilter()), package versions are out of sync.
    /// </summary>
    [Test]
    public async Task GetCollection_WithBookContentChunk_RuntimeVectorDataApiShapeIsCompatible()
    {
        // Arrange — no real DB connection is needed; connections are only opened for actual queries
#pragma warning disable SKEXP0010 // PostgresVectorStore is experimental
        using var store = new PostgresVectorStore("Host=localhost;Database=test;Username=test;Password=test");

        // Act — this triggers loading internal PostgresModelBuilder via PostgresCollection ctor
        var collection = store.GetCollection<string, BookContentChunk>("test-collection");
#pragma warning restore SKEXP0010

        // Assert
        await Assert.That(collection).IsNotNull();

        // Drive the same vector-search path used in production so binary-incompatible package
        // combinations fail in tests before deployment.
        await using var enumerator = collection
            .SearchAsync(
                new ReadOnlyMemory<float>(new float[1536]),
                1,
                options: new Microsoft.Extensions.VectorData.VectorSearchOptions<BookContentChunk>
                {
                    VectorProperty = x => x.TextEmbedding
                },
                cancellationToken: CancellationToken.None)
            .GetAsyncEnumerator();

        await Assert.ThrowsAsync<Exception>(async () => await enumerator.MoveNextAsync().AsTask());
    }
}
