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
    public async Task SessionWithManyUnknownPages_WritesOneWarningForTheWholeRequest()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-log-many");
        using HttpClient client = await ReadingTestSupport.CreateSignedInClientAsync(Factory, userId);
        const int unknownCount = 30;
        object[] intervals = Enumerable.Range(0, unknownCount)
            .Select(i => (object)new { pageKey = $"no-such-page-{i}", wordsRead = 10, activeSeconds = 10 })
            .Append(new { pageKey = ReadingTestSupport.LargePage, wordsRead = 10, activeSeconds = 10 })
            .ToArray();

        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/reading/session", new { intervals });

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        string[] about = _warnings.Messages
            .Where(m => m.Contains("unknown page", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        using (Assert.Multiple())
        {
            await Assert.That(about).Count().IsEqualTo(1)
                .Because("one warning per request, not one per interval: " + string.Join(" | ", about));
            await Assert.That(about.Single()).Contains(unknownCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

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
