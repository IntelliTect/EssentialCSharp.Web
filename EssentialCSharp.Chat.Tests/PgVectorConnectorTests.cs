using EssentialCSharp.Chat.Common.Models;
using Microsoft.SemanticKernel.Connectors.PgVector;

namespace EssentialCSharp.Chat.Tests;

public class PgVectorConnectorTests
{
    /// <summary>
    /// Verifies that PostgresVectorStore.GetCollection does not throw a TypeLoadException,
    /// which would indicate a version mismatch between Microsoft.SemanticKernel core and
    /// Microsoft.SemanticKernel.Connectors.PgVector (e.g., missing vtable slots on
    /// internal types like PostgresModelBuilder).
    /// </summary>
    [Test]
    public async Task GetCollection_WithBookContentChunk_DoesNotThrowTypeLoadException()
    {
        // Arrange — no real DB connection is needed; connections are only opened for actual queries
#pragma warning disable SKEXP0010 // PostgresVectorStore is experimental
        using var store = new PostgresVectorStore("Host=localhost;Database=test;Username=test;Password=test");

        // Act — this triggers loading internal PostgresModelBuilder via PostgresCollection ctor
        var collection = store.GetCollection<string, BookContentChunk>("test-collection");
#pragma warning restore SKEXP0010

        // Assert
        await Assert.That(collection).IsNotNull();
    }
}
