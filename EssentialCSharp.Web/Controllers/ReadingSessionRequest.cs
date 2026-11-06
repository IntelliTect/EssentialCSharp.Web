using System.ComponentModel.DataAnnotations;

namespace EssentialCSharp.Web.Controllers;

public sealed class ReadingSessionRequest
{
    /// <summary>
    /// Reading intervals to record. The upper bound is configured by
    /// <c>ReadingOptions.MaxIntervalsPerRequest</c> and enforced by the controller.
    /// </summary>
    [Required]
    [MinLength(1)]
    public List<ReadingIntervalRequest> Intervals { get; set; } = [];
}
