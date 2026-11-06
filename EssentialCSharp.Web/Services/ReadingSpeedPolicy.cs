namespace EssentialCSharp.Web.Services;

/// <summary>Words and seconds credited to a reader for one accepted interval.</summary>
public readonly record struct ReadingCredit(int Words, int Seconds);

/// <summary>
/// Pure outlier rules applied to each reported reading interval.
/// </summary>
public static class ReadingSpeedPolicy
{
    /// <summary>
    /// Decides how much of an interval to credit.
    /// Returns <see langword="null"/> when the interval is not usable (non-positive input or faster
    /// than <see cref="ReadingOptions.HardCutoffWpm"/>). Intervals slower than
    /// <see cref="ReadingOptions.SlowOutlierFactor"/> times <paramref name="referenceWpm"/> are credited
    /// with fewer seconds than their wall-clock time so idle stretches do not drag the average down.
    /// </summary>
    /// <param name="referenceWpm">The reader's current WPM, or 0 when unknown.</param>
    public static ReadingCredit? Evaluate(int words, int seconds, double referenceWpm, ReadingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (words <= 0 || seconds <= 0)
        {
            return null;
        }

        double intervalWpm = words / (seconds / 60.0);
        if (intervalWpm > options.HardCutoffWpm)
        {
            return null;
        }

        double slowThreshold = options.SlowOutlierFactor * referenceWpm;
        if (referenceWpm > 0 && intervalWpm < slowThreshold)
        {
            // words >= 1 and slowThreshold > 0 here, so the ceiling is at least 1.
            double clampedSeconds = Math.Ceiling(words / slowThreshold * 60.0);
            return new ReadingCredit(words, (int)Math.Min(seconds, clampedSeconds));
        }

        return new ReadingCredit(words, seconds);
    }
}
