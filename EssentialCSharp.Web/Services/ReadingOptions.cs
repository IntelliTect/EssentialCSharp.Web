namespace EssentialCSharp.Web.Services;

/// <summary>
/// Tunables for the reading-time tracker (mirrors Kindle's ReadingTimer values).
/// </summary>
public sealed class ReadingOptions
{
    public const string SectionName = "Reading";

    /// <summary>Intervals faster than this many words per minute are implausible and are ignored.</summary>
    public int HardCutoffWpm { get; set; } = 900;

    /// <summary>
    /// Intervals slower than this fraction of the reader's reference WPM are credited
    /// as if they had been read at exactly that fraction of the reference speed.
    /// </summary>
    public double SlowOutlierFactor { get; set; } = 0.25;

    /// <summary>Maximum number of intervals accepted in a single request.</summary>
    public int MaxIntervalsPerRequest { get; set; } = 50;
}
