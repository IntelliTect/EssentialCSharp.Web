using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// The outlier rules for a reported reading interval. Intervals faster than the hard cutoff are ignored; intervals
/// slower than <c>SlowOutlierFactor * referenceWpm</c> are credited as if read at that speed (so idle time does not
/// drag the reader's WPM down); everything else is credited as reported. The credited word count never changes.
/// The grid test states only invariants; the exact clamp arithmetic is pinned by the worked examples.
/// </summary>
public class ReadingSpeedPolicyTests
{
    private const double ReferenceWpm = 200;

    private static ReadingOptions Options => ReadingTestSupport.CreateOptions();

    private static double IntervalWpm(int words, int seconds) => words * 60.0 / seconds;

    public static IEnumerable<(int Words, int Seconds, double ReferenceWpm)> IntervalGrid()
    {
        foreach (int words in new[] { 1, 100, 400, 1000, 5000 })
        {
            foreach (int seconds in new[] { 1, 5, 30, 60, 300, 1800, 3600 })
            {
                foreach (double reference in new double[] { 0, 50, ReferenceWpm, 1000 })
                {
                    yield return (words, seconds, reference);
                }
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(IntervalGrid))]
    public async Task Evaluate_UpholdsTheCreditInvariants(int words, int seconds, double referenceWpm)
    {
        ReadingOptions options = Options;
        double intervalWpm = IntervalWpm(words, seconds);

        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(words, seconds, referenceWpm, options);

        if (intervalWpm > options.HardCutoffWpm)
        {
            await Assert.That(credit).IsNull().Because($"{intervalWpm:F0} WPM is above the {options.HardCutoffWpm} WPM cutoff");
            return;
        }

        await Assert.That(credit).IsNotNull();
        ReadingCredit value = credit!.Value;
        double creditedWpm = IntervalWpm(value.Words, value.Seconds);

        // Independent properties of any accepted interval, stated without re-deriving the clamp formula.
        using (Assert.Multiple())
        {
            await Assert.That(value.Words).IsEqualTo(words).Because("words are never rescaled");
            await Assert.That(value.Seconds).IsBetween(1, seconds).Because("credit only ever shortens the time, never below one second");
            await Assert.That(creditedWpm).IsGreaterThanOrEqualTo(intervalWpm - 1e-9).Because("crediting never makes the reader look slower");
            if (referenceWpm <= 0)
            {
                await Assert.That(value.Seconds).IsEqualTo(seconds).Because("without a reference speed nothing is a slow outlier");
            }
        }
    }

    /// <summary>Worked examples; <c>null</c> means the interval is ignored, otherwise the credited seconds (words are unchanged).</summary>
    [Test]
    // Hard cutoff 900 WPM: 300 words in 20 s is exactly 900 WPM (kept), 301 words is 903 WPM (ignored).
    [Arguments(300, 20, 200.0, 900, 0.25, 20)]
    [Arguments(301, 20, 200.0, 900, 0.25, null)]
    [Arguments(301, 20, 0.0, 900, 0.25, null)]
    // Slow threshold 0.25 * 200 = 50 WPM: exactly at it is kept; clearly or slightly below it is clamped to the threshold speed.
    [Arguments(50, 60, 200.0, 900, 0.25, 60)]
    [Arguments(100, 60, 200.0, 900, 0.25, 60)]
    [Arguments(100, 130, 200.0, 900, 0.25, 120)]
    [Arguments(100, 1200, 200.0, 900, 0.25, 120)]
    // The slow factor comes from the options: a 0.5 factor makes the threshold 100 WPM.
    [Arguments(100, 600, 200.0, 900, 0.5, 60)]
    // A factor of 0 disables slow-outlier handling; a factor of 1 treats anything below the reference as slow.
    [Arguments(10, 600, 200.0, 900, 0.0, 600)]
    [Arguments(100, 60, 200.0, 900, 1.0, 30)]
    [Arguments(100, 30, 200.0, 900, 1.0, 30)]
    // No reference speed: nothing is a slow outlier.
    [Arguments(10, 600, 0.0, 900, 0.25, 600)]
    // One word against a very fast reference rounds up to one whole second.
    [Arguments(1, 3600, 100_000.0, 900, 0.25, 1)]
    // Non-positive input is ignored.
    [Arguments(0, 10, 200.0, 900, 0.25, null)]
    [Arguments(10, 0, 200.0, 900, 0.25, null)]
    [Arguments(-5, 10, 200.0, 900, 0.25, null)]
    [Arguments(10, -5, 200.0, 900, 0.25, null)]
    public async Task Evaluate_WorkedExamples(
        int words, int seconds, double referenceWpm, int hardCutoffWpm, double slowFactor, int? expectedSeconds)
    {
        var options = new ReadingOptions { HardCutoffWpm = hardCutoffWpm, SlowOutlierFactor = slowFactor };

        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(words, seconds, referenceWpm, options);

        ReadingCredit? expected = expectedSeconds is { } s ? new ReadingCredit(words, s) : null;
        await Assert.That(credit).IsEqualTo(expected);
    }
}
