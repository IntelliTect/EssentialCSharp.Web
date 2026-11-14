using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Controllers;

public sealed class ReadingSessionRequest : IValidatableObject
{
    /// <summary>
    /// Reading intervals to record. The upper bound is configured by
    /// <c>ReadingOptions.MaxIntervalsPerRequest</c>.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<ReadingIntervalRequest> Intervals { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        int maxIntervals = validationContext.GetRequiredService<IOptions<ReadingOptions>>().Value.MaxIntervalsPerRequest;
        if (Intervals.Count > maxIntervals)
        {
            yield return new ValidationResult(
                $"A session may contain at most {maxIntervals} intervals.",
                [nameof(Intervals)]);
        }

        for (int i = 0; i < Intervals.Count; i++)
        {
            if (Intervals[i] is null)
            {
                yield return new ValidationResult(
                    "Intervals must not contain null entries.",
                    [$"{nameof(Intervals)}[{i}]"]);
            }
        }
    }
}
