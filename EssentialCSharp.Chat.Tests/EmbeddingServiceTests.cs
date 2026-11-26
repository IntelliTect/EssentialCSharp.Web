using EssentialCSharp.Chat.Common.Models;
using EssentialCSharp.Chat.Common.Services;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.VectorData;
using Moq;

namespace EssentialCSharp.Chat.Tests;

public class EmbeddingServiceTests
{
    // Regression test: interactive query embeddings used to share one process-wide, paced queue with batch
    // rebuilds, so a burst of MCP tool calls from one user stalled every other user's search behind it.
    [Test]
    public async Task GenerateEmbeddingAsync_ConcurrentQueries_RunConcurrentlyRatherThanQueueing()
    {
        const int concurrentQueries = 5;
        int started = 0;
        TaskCompletionSource allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        Mock<IEmbeddingGenerator<string, Embedding<float>>> generator = new();
        generator
            .Setup(g => g.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .Returns(async (IEnumerable<string> _, EmbeddingGenerationOptions? _, CancellationToken cancellationToken) =>
            {
                if (Interlocked.Increment(ref started) == concurrentQueries)
                {
                    allStarted.TrySetResult();
                }
                await release.Task.WaitAsync(cancellationToken);
                return new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(new float[8])]);
            });
        EmbeddingService service = new(
            new Mock<VectorStore>().Object,
            generator.Object,
            Options.Create(new EmbeddingRetryOptions { MinInterRequestDelayMs = 250 }));

        Task<ReadOnlyMemory<float>>[] queries = [.. Enumerable.Range(0, concurrentQueries)
            .Select(i => service.GenerateEmbeddingAsync($"query {i}"))];

        // With a shared serialized queue only one request would be in flight, and this would time out.
        Task finished = await Task.WhenAny(allStarted.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        release.TrySetResult();
        await Task.WhenAll(queries);

        await Assert.That(finished).IsSameReferenceAs(allStarted.Task);
    }
}
