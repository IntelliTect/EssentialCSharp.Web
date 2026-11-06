using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Tunables for the reading-time tracker (mirrors Kindle's ReadingTimer values).
/// This is the single source for every reading constant: the client receives the relevant
/// values through <c>window.READING_CONTEXT</c> and keeps no copies of its own.
/// </summary>
public sealed class ReadingOptions
{
    public const string SectionName = "Reading";

    /// <summary>Intervals faster than this many words per minute are implausible and are ignored.</summary>
    [Range(1, 10_000)]
    public int HardCutoffWpm { get; init; } = 900;

    /// <summary>
    /// Intervals slower than this fraction of the reader's reference WPM are credited
    /// as if they had been read at exactly that fraction of the reference speed.
    /// </summary>
    [Range(0.0, 1.0)]
    public double SlowOutlierFactor { get; init; } = 0.25;

    /// <summary>Maximum number of intervals accepted in a single request.</summary>
    [Range(1, 1_000)]
    public int MaxIntervalsPerRequest { get; init; } = 50;

    /// <summary>Reading speed (words per minute) the client assumes until the reader has a derived speed.</summary>
    [Range(1, 10_000)]
    public int DefaultWpm { get; init; } = 200;

    /// <summary>
    /// Client-side ceiling on how fast scrolling may be credited as reading (words per minute).
    /// Must not exceed <see cref="HardCutoffWpm"/>, otherwise the server would discard intervals the client credits.
    /// </summary>
    [Range(1, 10_000)]
    public int MaxCreditWpm { get; init; } = 800;

    /// <summary>Seconds without user activity after which the client stops counting active time.</summary>
    [Range(1, 86_400)]
    public int IdleThresholdSeconds { get; init; } = 300;

    /// <summary>Active seconds after which the client flushes an interval while the reader stays on the page.</summary>
    [Range(1, 3_600)]
    public int PeriodicFlushActiveSeconds { get; init; } = 60;

    /// <summary>Largest <c>activeSeconds</c> the server accepts for one interval; larger values are rejected.</summary>
    [Range(1, 86_400)]
    public int MaxActiveSecondsPerInterval { get; init; } = 1800;
}

/// <summary>Cross-field validation that data annotations cannot express.</summary>
public sealed class ReadingOptionsValidator : IValidateOptions<ReadingOptions>
{
    public ValidateOptionsResult Validate(string? name, ReadingOptions options)
    {
        List<string>? failures = null;
        if (options.MaxCreditWpm > options.HardCutoffWpm)
        {
            (failures ??= []).Add(
                $"{nameof(ReadingOptions.MaxCreditWpm)} ({options.MaxCreditWpm}) must not exceed {nameof(ReadingOptions.HardCutoffWpm)} ({options.HardCutoffWpm}).");
        }

        if (options.PeriodicFlushActiveSeconds > options.MaxActiveSecondsPerInterval)
        {
            (failures ??= []).Add(
                $"{nameof(ReadingOptions.PeriodicFlushActiveSeconds)} must not exceed {nameof(ReadingOptions.MaxActiveSecondsPerInterval)}.");
        }

        return failures is null ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
