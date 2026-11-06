using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Worked examples for the interval outlier rules. All numbers are literal: with a 900 WPM cutoff, a 0.25 slow factor
/// and a 200 WPM reference reader, intervals under 50 WPM are slow outliers and intervals over 900 WPM are ignored.
/// </summary>
public class ReadingSpeedPolicyTests
{
    private static readonly ReadingOptions Options = new() { HardCutoffWpm = 900, SlowOutlierFactor = 0.25 };

    private const double ReferenceWpm = 200;

    [Test]
    public async Task Evaluate_IntervalAtHardCutoff_IsCreditedInFull()
    {
        // 300 words in 20 s is exactly 900 WPM.
        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(300, 20, ReferenceWpm, Options);

        await Assert.That(credit).IsEqualTo(new ReadingCredit(300, 20));
    }

    [Test]
    public async Task Evaluate_IntervalJustAboveHardCutoff_IsIgnored()
    {
        // 301 words in 20 s is 903 WPM.
        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(301, 20, ReferenceWpm, Options);

        await Assert.That(credit).IsNull();
    }

    [Test]
    public async Task Evaluate_IntervalClearlyAboveSlowThreshold_IsCreditedInFull()
    {
        // 100 words in 60 s is 100 WPM, twice the 50 WPM slow threshold.
        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(100, 60, ReferenceWpm, Options);

        await Assert.That(credit).IsEqualTo(new ReadingCredit(100, 60));
    }

    [Test]
    public async Task Evaluate_IntervalClearlyBelowSlowThreshold_IsCreditedAsIfReadAtTheThreshold()
    {
        // 100 words in 1200 s is 5 WPM. At the 50 WPM threshold those 100 words take 120 s.
        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(100, 1200, ReferenceWpm, Options);

        await Assert.That(credit).IsEqualTo(new ReadingCredit(100, 120));
    }

    [Test]
    public async Task Evaluate_IntervalJustBelowSlowThreshold_IsClamped()
    {
        // 100 words in 130 s is 46.2 WPM, just under the 50 WPM threshold; at 50 WPM those words take 120 s.
        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(100, 130, ReferenceWpm, Options);

        await Assert.That(credit).IsEqualTo(new ReadingCredit(100, 120));
    }

    [Test]
    public async Task Evaluate_SlowFactorComesFromOptions()
    {
        var lenient = new ReadingOptions { HardCutoffWpm = 900, SlowOutlierFactor = 0.5 };

        // Threshold is now 100 WPM: 100 words in 600 s (10 WPM) are credited 60 s.
        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(100, 600, ReferenceWpm, lenient);

        await Assert.That(credit).IsEqualTo(new ReadingCredit(100, 60));
    }

    [Test]
    public async Task Evaluate_SlowIntervalWithNoReference_IsCreditedInFull()
    {
        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(10, 600, 0, Options);

        await Assert.That(credit).IsEqualTo(new ReadingCredit(10, 600));
    }

    [Test]
    public async Task Evaluate_OneWordAgainstVeryFastReference_IsCreditedExactlyOneSecond()
    {
        // Threshold 25 000 WPM: one word takes 0.0024 s, rounded up to a whole second.
        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(1, 3600, 100_000, Options);

        await Assert.That(credit).IsEqualTo(new ReadingCredit(1, 1));
    }

    [Test]
    [Arguments(0, 10)]
    [Arguments(10, 0)]
    [Arguments(-5, 10)]
    [Arguments(10, -5)]
    public async Task Evaluate_NonPositiveInput_IsIgnored(int words, int seconds)
    {
        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(words, seconds, ReferenceWpm, Options);

        await Assert.That(credit).IsNull();
    }

    [Test]
    public async Task Evaluate_AcrossManyIntervals_NeverCreditsMoreTimeThanElapsedOrLessSpeedThanTheFloor()
    {
        const double floorWpm = 50; // 0.25 * 200
        var failures = new List<string>();

        foreach (int words in new[] { 100, 400, 1000, 5000 })
        {
            foreach (int seconds in new[] { 5, 30, 60, 300, 1800, 3600 })
            {
                ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(words, seconds, ReferenceWpm, Options);
                double intervalWpm = words * 60.0 / seconds;
                if (credit is null)
                {
                    if (intervalWpm <= 900)
                    {
                        failures.Add($"{words}w/{seconds}s ({intervalWpm:F0} WPM) was ignored");
                    }
                    continue;
                }

                if (credit.Value.Words != words) failures.Add($"{words}w/{seconds}s credited {credit.Value.Words} words");
                if (credit.Value.Seconds > seconds) failures.Add($"{words}w/{seconds}s credited {credit.Value.Seconds} s, more than elapsed");
                if (credit.Value.Seconds < 1) failures.Add($"{words}w/{seconds}s credited no time");
                double creditedWpm = words * 60.0 / credit.Value.Seconds;
                // Rounding the clamped time up to a whole second may land slightly under the floor, never meaningfully so.
                if (creditedWpm < floorWpm * 0.99) failures.Add($"{words}w/{seconds}s credited at {creditedWpm:F1} WPM, below the {floorWpm} WPM floor");
            }
        }

        await Assert.That(failures).IsEmpty();
    }
}
