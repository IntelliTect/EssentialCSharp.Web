using Microsoft.Extensions.Logging;

namespace EssentialCSharp.Web.Tests;

internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception)
{
    /// <summary>Everything a log provider could write for this entry: the message plus the full exception text including inner exceptions.</summary>
    public string FullText => Exception is null ? Message : $"{Message}{Environment.NewLine}{Exception}";
}

internal sealed class CapturingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    /// <summary>All captured output joined, as a provider writing messages and exceptions would produce it.</summary>
    public string AllText => string.Join(Environment.NewLine, Entries.Select(e => e.FullText));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
}
