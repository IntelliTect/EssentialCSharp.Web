using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

public class ReadingSpeedPolicyTests
{
    private static readonly ReadingOptions Options = new();

    private const double ReferenceWpm = 200;

    /// <summary>Seconds needed to read <paramref name="words"/> at exactly <paramref name="wpm"/>.</summary>
    private static double SecondsAt(int words, double wpm) => words / wpm * 60.0;

    [Test]
    public async Task Evaluate_IntervalAtHardCutoff_IsCreditedInFull()
    {
        const int seconds = 20;
        int words = (int)(Options.HardCutoffWpm * seconds / 60.0);

        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(words, seconds, ReferenceWpm, Options);

        await Assert.That(credit).IsEqualTo(new ReadingCredit(words, seconds));
    }

    [Test]
    public async Task Evaluate_IntervalJustAboveHardCutoff_IsIgnored()
    {
        const int seconds = 20;
        int words = (int)(Options.HardCutoffWpm * seconds / 60.0) + 1;

        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(words, seconds, ReferenceWpm, Options);

        await Assert.That(credit).IsNull();
    }

    [Test]
    public async Task Evaluate_IntervalJustAboveSlowThreshold_IsNotClamped()
    {
        double thresholdWpm = Options.SlowOutlierFactor * ReferenceWpm;
        const int words = 100;
        int seconds = (int)Math.Floor(SecondsAt(words, thresholdWpm));

        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(words, seconds, ReferenceWpm, Options);

        await Assert.That(credit).IsEqualTo(new ReadingCredit(words, seconds));
    }

    [Test]
    public async Task Evaluate_VerySlowInterval_CountsForLessThanItsWallClockTime()
    {
        const int words = 100;
        int seconds = (int)(SecondsAt(words, ReferenceWpm) * 20);

        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(words, seconds, ReferenceWpm, Options);

        await Assert.That(credit).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(credit!.Value.Words).IsEqualTo(words);
            await Assert.That(credit.Value.Seconds).IsLessThan(seconds);
            // Credited as if read at the slow-outlier floor speed.
            double creditedWpm = credit.Value.Words / (credit.Value.Seconds / 60.0);
            await Assert.That(creditedWpm).IsEqualTo(Options.SlowOutlierFactor * ReferenceWpm).Within(1.0);
        }
    }

    [Test]
    public async Task Evaluate_SlowIntervalWithNoReference_IsCreditedInFull()
    {
        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(10, 600, 0, Options);

        await Assert.That(credit).IsEqualTo(new ReadingCredit(10, 600));
    }

    [Test]
    public async Task Evaluate_TinySlowInterval_IsCreditedWithAtLeastOneSecond()
    {
        ReadingCredit? credit = ReadingSpeedPolicy.Evaluate(1, 3600, 100_000, Options);

        await Assert.That(credit).IsNotNull();
        await Assert.That(credit!.Value.Seconds).IsGreaterThanOrEqualTo(1);
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
    public async Task Evaluate_RespectsConfiguredCutoff()
    {
        var strict = new ReadingOptions { HardCutoffWpm = 100 };

        ReadingCredit? atLimit = ReadingSpeedPolicy.Evaluate(100, 60, 0, strict);
        ReadingCredit? overLimit = ReadingSpeedPolicy.Evaluate(101, 60, 0, strict);

        using (Assert.Multiple())
        {
            await Assert.That(atLimit).IsNotNull();
            await Assert.That(overLimit).IsNull();
        }
    }
}
