using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// The page keys in a reading session come from the client, so the amount written to the log at Warning must not
/// grow with the number of intervals a caller sends.
/// </summary>
public sealed class ReadingLoggingTests : IntegrationTestBase, IDisposable
{
    private readonly WarningCollector _warnings = new();

    public void Dispose() => _warnings.Dispose();

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReadingTestSupport.UseFakes(services, new ReadingTestSupport.FakeWordCountService());
        services.AddSingleton<ILoggerProvider>(_warnings);
    }

    [Test]
    public async Task UnknownPages_AreReported_ButTheWarningVolumeDoesNotGrowWithTheirNumber()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-log-many");
        using HttpClient client = await ReadingTestSupport.CreateSignedInClientAsync(Factory, userId);

        int forOne = await UnknownPageWarningsForAsync(client, unknownCount: 1);
        int forMany = await UnknownPageWarningsForAsync(client, unknownCount: Controllers.ReadingSessionRequest.MaxIntervals - 1);

        using (Assert.Multiple())
        {
            await Assert.That(forOne).IsGreaterThan(0).Because("unknown page keys must not be dropped silently");
            await Assert.That(forMany).IsEqualTo(forOne).Because("a caller must not be able to multiply log volume by sending more intervals");
        }
    }

    /// <summary>Posts a session with the given number of unknown pages (plus one known page) and counts the new warnings about them.</summary>
    private async Task<int> UnknownPageWarningsForAsync(HttpClient client, int unknownCount)
    {
        int before = CountUnknownPageWarnings();
        object[] intervals = Enumerable.Range(0, unknownCount)
            .Select(i => (object)new { pageKey = $"no-such-page-{unknownCount}-{i}", wordsRead = 10, activeSeconds = 10 })
            .Append(new { pageKey = ReadingTestSupport.LargePage, wordsRead = 10, activeSeconds = 10 })
            .ToArray();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/reading/session", new { intervals });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return CountUnknownPageWarnings() - before;
    }

    private int CountUnknownPageWarnings() =>
        _warnings.Messages.Count(m => m.Contains("unknown page", StringComparison.OrdinalIgnoreCase));

    [Test]
    public async Task SessionWithOnlyKnownPages_WritesNoUnknownPageWarning()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-log-none");
        using HttpClient client = await ReadingTestSupport.CreateSignedInClientAsync(Factory, userId);

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/reading/session",
            new { intervals = new[] { new { pageKey = ReadingTestSupport.LargePage, wordsRead = 10, activeSeconds = 10 } } });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(_warnings.Messages.Where(m => m.Contains("unknown page", StringComparison.OrdinalIgnoreCase))).IsEmpty();
    }

    private sealed class WarningCollector : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Warning)
            {
                _messages.Enqueue(formatter(state, exception));
            }
        }

        public void Dispose()
        {
        }
    }
}
