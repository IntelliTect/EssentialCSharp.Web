using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

public class ReadingOptionsValidatorTests
{
    private static ValidateOptionsResult CrossField(ReadingOptions options) =>
        new ReadingOptionsValidator().Validate(null, options);

    private static List<ValidationResult> Annotations(ReadingOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }

    [Test]
    public async Task Defaults_Pass()
    {
        var options = new ReadingOptions();

        using (Assert.Multiple())
        {
            await Assert.That(CrossField(options).Succeeded).IsTrue();
            await Assert.That(Annotations(options)).IsEmpty();
        }
    }

    [Test]
    public async Task MaxCreditWpmAboveHardCutoff_Fails()
    {
        ValidateOptionsResult result = CrossField(new ReadingOptions { HardCutoffWpm = 700, MaxCreditWpm = 701 });

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains(nameof(ReadingOptions.MaxCreditWpm));
    }

    [Test]
    public async Task MaxCreditWpmEqualToHardCutoff_Passes() =>
        await Assert.That(CrossField(new ReadingOptions { HardCutoffWpm = 700, MaxCreditWpm = 700 }).Succeeded).IsTrue();

    [Test]
    public async Task PeriodicFlushAboveMaxActiveSeconds_Fails()
    {
        ValidateOptionsResult result = CrossField(
            new ReadingOptions { MaxActiveSecondsPerInterval = 100, IdleThresholdSeconds = 100, PeriodicFlushActiveSeconds = 101 });

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains(nameof(ReadingOptions.PeriodicFlushActiveSeconds));
    }

    [Test]
    public async Task IdleThresholdAboveMaxActiveSeconds_Fails()
    {
        ValidateOptionsResult result = CrossField(
            new ReadingOptions { MaxActiveSecondsPerInterval = 100, PeriodicFlushActiveSeconds = 60, IdleThresholdSeconds = 101 });

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains(nameof(ReadingOptions.IdleThresholdSeconds));
    }

    [Test]
    public async Task MaxIntervalsPerRequestBelowClientOutbox_Fails()
    {
        ValidateOptionsResult result = CrossField(new ReadingOptions { MaxIntervalsPerRequest = 19 });

        await Assert.That(result.Failed).IsTrue();
        await Assert.That(result.FailureMessage).Contains(nameof(ReadingOptions.MaxIntervalsPerRequest));
    }

    [Test]
    public async Task MaxIntervalsPerRequestEqualToClientOutbox_Passes() =>
        await Assert.That(CrossField(new ReadingOptions { MaxIntervalsPerRequest = 20 }).Succeeded).IsTrue();

    [Test]
    [Arguments(nameof(ReadingOptions.HardCutoffWpm), 0)]
    [Arguments(nameof(ReadingOptions.HardCutoffWpm), 10_001)]
    [Arguments(nameof(ReadingOptions.MaxIntervalsPerRequest), 0)]
    [Arguments(nameof(ReadingOptions.MaxIntervalsPerRequest), 1_001)]
    [Arguments(nameof(ReadingOptions.DefaultWpm), 0)]
    [Arguments(nameof(ReadingOptions.DefaultWpm), 10_001)]
    [Arguments(nameof(ReadingOptions.MaxCreditWpm), 0)]
    [Arguments(nameof(ReadingOptions.MaxCreditWpm), 10_001)]
    [Arguments(nameof(ReadingOptions.IdleThresholdSeconds), 0)]
    [Arguments(nameof(ReadingOptions.IdleThresholdSeconds), 86_401)]
    [Arguments(nameof(ReadingOptions.PeriodicFlushActiveSeconds), 0)]
    [Arguments(nameof(ReadingOptions.PeriodicFlushActiveSeconds), 3_601)]
    [Arguments(nameof(ReadingOptions.MaxActiveSecondsPerInterval), 0)]
    [Arguments(nameof(ReadingOptions.MaxActiveSecondsPerInterval), 86_401)]
    public async Task IntegerOutOfRange_IsRejected(string property, int value)
    {
        ReadingOptions options = With(property, value);

        await Assert.That(Annotations(options).SelectMany(r => r.MemberNames)).Contains(property);
    }

    [Test]
    [Arguments(-0.01)]
    [Arguments(1.01)]
    public async Task SlowOutlierFactorOutOfRange_IsRejected(double value)
    {
        var options = new ReadingOptions { SlowOutlierFactor = value };

        await Assert.That(Annotations(options).SelectMany(r => r.MemberNames)).Contains(nameof(ReadingOptions.SlowOutlierFactor));
    }

    [Test]
    [Arguments(nameof(ReadingOptions.HardCutoffWpm), 1)]
    [Arguments(nameof(ReadingOptions.HardCutoffWpm), 10_000)]
    [Arguments(nameof(ReadingOptions.IdleThresholdSeconds), 86_400)]
    [Arguments(nameof(ReadingOptions.PeriodicFlushActiveSeconds), 3_600)]
    [Arguments(nameof(ReadingOptions.MaxActiveSecondsPerInterval), 86_400)]
    public async Task IntegerAtRangeBoundary_IsAccepted(string property, int value) =>
        await Assert.That(Annotations(With(property, value))).IsEmpty();

    private static ReadingOptions With(string property, int value) => property switch
    {
        nameof(ReadingOptions.HardCutoffWpm) => new ReadingOptions { HardCutoffWpm = value },
        nameof(ReadingOptions.MaxIntervalsPerRequest) => new ReadingOptions { MaxIntervalsPerRequest = value },
        nameof(ReadingOptions.DefaultWpm) => new ReadingOptions { DefaultWpm = value },
        nameof(ReadingOptions.MaxCreditWpm) => new ReadingOptions { MaxCreditWpm = value },
        nameof(ReadingOptions.IdleThresholdSeconds) => new ReadingOptions { IdleThresholdSeconds = value },
        nameof(ReadingOptions.PeriodicFlushActiveSeconds) => new ReadingOptions { PeriodicFlushActiveSeconds = value },
        nameof(ReadingOptions.MaxActiveSecondsPerInterval) => new ReadingOptions { MaxActiveSecondsPerInterval = value },
        _ => throw new ArgumentOutOfRangeException(nameof(property)),
    };
}
