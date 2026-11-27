using System.ComponentModel.DataAnnotations;

namespace EssentialCSharp.Web.Controllers;

public sealed class ReadingSessionRequest : IValidatableObject
{
    /// <summary>
    /// Most intervals accepted in one request. It is also how many unsent intervals the client keeps in memory (sent to the
    /// client as <c>READING_CONTEXT.maxQueuedIntervals</c>), so everything it holds always fits in one request.
    /// </summary>
    public const int MaxIntervals = 20;

    /// <summary>Reading intervals to record.</summary>
    [Required]
    [MinLength(1)]
    [MaxLength(MaxIntervals)]
    public List<ReadingIntervalRequest> Intervals { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
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
