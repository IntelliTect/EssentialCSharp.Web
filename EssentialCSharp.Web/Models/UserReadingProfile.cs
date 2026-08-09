using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;

namespace EssentialCSharp.Web.Models;

/// <summary>
/// Aggregate reading profile for a single user.
/// WPM is always derived as <c>TotalWordsRead / (TotalActiveSeconds / 60.0)</c>;
/// it is never stored directly so there is no floating-point drift over time.
/// </summary>
public class UserReadingProfile
{
    /// <summary>Primary key — also the FK to <see cref="EssentialCSharpWebUser"/>.</summary>
    [Required]
    [MaxLength(450)]
    public required string UserId { get; set; }

    /// <summary>
    /// Cumulative prose words credited across all accepted, clamped reading intervals.
    /// </summary>
    public long TotalWordsRead { get; set; }

    /// <summary>
    /// Cumulative active seconds across all accepted, clamped reading intervals.
    /// </summary>
    public long TotalActiveSeconds { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public EssentialCSharpWebUser? User { get; set; }

    /// <summary>
    /// Derives the current words-per-minute estimate. Returns null if insufficient data.
    /// </summary>
    public double? DeriveWpm() =>
        TotalActiveSeconds > 0 ? TotalWordsRead / (TotalActiveSeconds / 60.0) : null;
}
