using EssentialCSharp.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// A misconfigured <c>Reading:</c> section must stop the host at startup instead of surfacing as odd behavior later.
/// </summary>
public class ReadingOptionsStartupTests : IntegrationTestBase
{
    [Test]
    [Arguments("Reading:HardCutoffWpm", "0")]   // violates a data-annotation range
    [Arguments("Reading:MaxIntervalsPerRequest", "5")] // in range, violates only a cross-field rule (below the client outbox size)
    public async Task InvalidReadingConfiguration_PreventsTheHostFromStarting(string key, string value)
    {
        // WebApplicationFactory runs the app's entry point on a background thread and races that thread's failure
        // against its own "entry point exited without building a host" error, so the exception that reaches the test
        // thread is not reliably the original one (it differs between Windows and Linux). The host always logs the
        // real startup failure before rethrowing, so assert on what it logged rather than on what propagated.
        var loggedExceptions = new LoggedExceptionCollector();
        using var factory = Factory.Inner.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }));
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(loggedExceptions));
        });

        Exception? failure = null;
        try
        {
            using HttpClient client = McpTestHelper.CreateClient(factory);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        await Assert.That(failure).IsNotNull().Because("an invalid Reading configuration must fail host startup");
        await Assert.That(loggedExceptions.Exceptions.SelectMany(ExceptionChain).Concat(ExceptionChain(failure!)))
            .Contains(e => e is OptionsValidationException);
    }

    private static IEnumerable<Exception> ExceptionChain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private sealed class LoggedExceptionCollector : ILoggerProvider, ILogger
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<Exception> _exceptions = new();

        public IReadOnlyCollection<Exception> Exceptions => _exceptions;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null)
            {
                _exceptions.Enqueue(exception);
            }
        }

        public void Dispose()
        {
        }
    }
}
