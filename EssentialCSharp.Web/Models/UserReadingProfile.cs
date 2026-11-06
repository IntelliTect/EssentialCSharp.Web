using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web.Models;

/// <summary>
/// Aggregate reading profile for a single user.
/// WPM is always derived as <c>TotalWordsRead / (TotalActiveSeconds / 60.0)</c>;
/// it is never stored directly so there is no floating-point drift over time.
/// </summary>
[PrimaryKey(nameof(UserId))]
public class UserReadingProfile
{
    /// <summary>Primary key; also the FK to <see cref="EssentialCSharpWebUser"/>.</summary>
    [MaxLength(450)]
    public required string UserId { get; set; }

    /// <summary>Cumulative prose words credited across all accepted reading intervals.</summary>
    public long TotalWordsRead { get; set; }

    /// <summary>Cumulative active seconds credited across all accepted reading intervals.</summary>
    public long TotalActiveSeconds { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    [ForeignKey(nameof(UserId))]
    public EssentialCSharpWebUser? User { get; set; }

    /// <summary>Derives the words-per-minute estimate. Returns null if there is no active time.</summary>
    public double? DeriveWpm() => DeriveWpm(TotalWordsRead, TotalActiveSeconds);

    public static double? DeriveWpm(long totalWordsRead, long totalActiveSeconds) =>
        totalActiveSeconds > 0 ? totalWordsRead / (totalActiveSeconds / 60.0) : null;
}
