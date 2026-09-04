using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web.Models;

/// <summary>
/// Records a single reading interval for a content page.
/// This is an audit/detail trail; the WPM math reads only
/// <see cref="UserReadingProfile"/>.
/// </summary>
[Index(nameof(UserId), nameof(RecordedAtUtc))]
[Index(nameof(UserId), nameof(PageKey))]
public class ReadingActivity
{
    public int Id { get; set; }

    [Required]
    [MaxLength(450)]
    public required string UserId { get; set; }

    /// <summary>
    /// Matches <see cref="SiteMapping.PrimaryKey"/> for the page that was read.
    /// </summary>
    [Required]
    [MaxLength(256)]
    public required string PageKey { get; set; }

    public DateTime RecordedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Active (non-idle, non-hidden) seconds spent on this page.</summary>
    public int ActiveSeconds { get; set; }

    /// <summary>Prose words credited to this reading interval (may be fractional for partial reads, rounded to int).</summary>
    public int WordsRead { get; set; }

    /// <summary>True when the reader reached the bottom of the page content.</summary>
    public bool Completed { get; set; }

    public EssentialCSharpWebUser? User { get; set; }
}
