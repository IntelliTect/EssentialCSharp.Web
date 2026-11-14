using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

public class ReadingOptionsValidatorTests
{
    private static ValidateOptionsResult CrossField(ReadingOptions options) =>
        new ReadingOptionsValidator().Validate(null, options);

    private static List<string> AnnotatedMembersWithErrors(ReadingOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results.SelectMany(r => r.MemberNames).ToList();
    }

    /// <summary>
    /// Each integer option with its inclusive valid range and a way to set just that option. These bounds are the
    /// documented contract of the options (a configured value outside them must be rejected), deliberately restated
    /// here rather than read back from the <c>[Range]</c> attributes, which would only test the attributes against themselves.
    /// </summary>
    private static readonly (string Property, int Min, int Max, Func<int, ReadingOptions> With)[] IntegerRanges =
    [
        (nameof(ReadingOptions.HardCutoffWpm), 1, 10_000, v => new ReadingOptions { HardCutoffWpm = v }),
        (nameof(ReadingOptions.MaxIntervalsPerRequest), 1, 1_000, v => new ReadingOptions { MaxIntervalsPerRequest = v }),
        (nameof(ReadingOptions.DefaultWpm), 1, 10_000, v => new ReadingOptions { DefaultWpm = v }),
        (nameof(ReadingOptions.MaxCreditWpm), 1, 10_000, v => new ReadingOptions { MaxCreditWpm = v }),
        (nameof(ReadingOptions.IdleThresholdSeconds), 1, 86_400, v => new ReadingOptions { IdleThresholdSeconds = v }),
        (nameof(ReadingOptions.PeriodicFlushActiveSeconds), 1, 3_600, v => new ReadingOptions { PeriodicFlushActiveSeconds = v }),
        (nameof(ReadingOptions.MaxActiveSecondsPerInterval), 1, 86_400, v => new ReadingOptions { MaxActiveSecondsPerInterval = v }),
    ];

    /// <summary>Every integer option just outside (<c>valid: false</c>) and exactly on (<c>valid: true</c>) both bounds.</summary>
    public static IEnumerable<(string Property, int Value, Func<int, ReadingOptions> With, bool Valid)> IntegerBoundaryCases()
    {
        foreach ((string property, int min, int max, Func<int, ReadingOptions> with) in IntegerRanges)
        {
            yield return (property, min - 1, with, false);
            yield return (property, min, with, true);
            yield return (property, max, with, true);
            yield return (property, max + 1, with, false);
        }
    }

    [Test]
    public async Task Defaults_Pass()
    {
        var options = new ReadingOptions();

        using (Assert.Multiple())
        {
            await Assert.That(CrossField(options).Succeeded).IsTrue();
            await Assert.That(AnnotatedMembersWithErrors(options)).IsEmpty();
        }
    }

    [Test]
    [MethodDataSource(nameof(IntegerBoundaryCases))]
    public async Task IntegerOption_IsAcceptedWithinItsRangeAndRejectedOutsideIt(
        string property, int value, Func<int, ReadingOptions> with, bool valid)
    {
        List<string> membersWithErrors = AnnotatedMembersWithErrors(with(value));

        if (valid)
        {
            await Assert.That(membersWithErrors).DoesNotContain(property);
        }
        else
        {
            await Assert.That(membersWithErrors).Contains(property);
        }
    }

    [Test]
    [Arguments(-0.01, false)]
    [Arguments(0.0, true)]
    [Arguments(0.25, true)]
    [Arguments(1.0, true)]
    [Arguments(1.01, false)]
    public async Task SlowOutlierFactor_IsAcceptedWithinZeroToOneInclusive(double value, bool valid)
    {
        List<string> membersWithErrors = AnnotatedMembersWithErrors(new ReadingOptions { SlowOutlierFactor = value });

        if (valid)
        {
            await Assert.That(membersWithErrors).DoesNotContain(nameof(ReadingOptions.SlowOutlierFactor));
        }
        else
        {
            await Assert.That(membersWithErrors).Contains(nameof(ReadingOptions.SlowOutlierFactor));
        }
    }

    /// <summary>Cross-field rules: each case names the option the failure message must mention, or null when it is valid.</summary>
    public static IEnumerable<(Func<ReadingOptions> Options, string? FailingProperty)> CrossFieldCases()
    {
        yield return (() => new ReadingOptions { HardCutoffWpm = 700, MaxCreditWpm = 701 }, nameof(ReadingOptions.MaxCreditWpm));
        yield return (() => new ReadingOptions { HardCutoffWpm = 700, MaxCreditWpm = 700 }, null);

        yield return (
            () => new ReadingOptions { MaxActiveSecondsPerInterval = 100, IdleThresholdSeconds = 100, PeriodicFlushActiveSeconds = 101 },
            nameof(ReadingOptions.PeriodicFlushActiveSeconds));
        yield return (
            () => new ReadingOptions { MaxActiveSecondsPerInterval = 100, IdleThresholdSeconds = 100, PeriodicFlushActiveSeconds = 100 },
            null);

        yield return (
            () => new ReadingOptions { MaxActiveSecondsPerInterval = 100, PeriodicFlushActiveSeconds = 60, IdleThresholdSeconds = 101 },
            nameof(ReadingOptions.IdleThresholdSeconds));

        yield return (
            () => new ReadingOptions { MaxIntervalsPerRequest = ReadingOptionsValidator.MinIntervalsPerRequest - 1 },
            nameof(ReadingOptions.MaxIntervalsPerRequest));
        yield return (
            () => new ReadingOptions { MaxIntervalsPerRequest = ReadingOptionsValidator.MinIntervalsPerRequest },
            null);
    }

    [Test]
    [MethodDataSource(nameof(CrossFieldCases))]
    public async Task CrossFieldRules_FailNamingTheOffendingOptionOnlyWhenViolated(
        Func<ReadingOptions> options, string? failingProperty)
    {
        ValidateOptionsResult result = CrossField(options());

        if (failingProperty is null)
        {
            await Assert.That(result.Succeeded).IsTrue();
        }
        else
        {
            using (Assert.Multiple())
            {
                await Assert.That(result.Failed).IsTrue();
                await Assert.That(result.FailureMessage).Contains(failingProperty);
            }
        }
    }
}
