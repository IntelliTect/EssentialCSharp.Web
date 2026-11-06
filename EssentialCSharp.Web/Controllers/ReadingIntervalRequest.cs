using System.ComponentModel.DataAnnotations;

namespace EssentialCSharp.Web.Controllers;

public sealed class ReadingIntervalRequest
{
    public const int PageKeyMaxLength = 256;
    public const int MaxActiveSeconds = 24 * 60 * 60;
    public const int MaxWordsRead = 100_000;

    /// <summary>The page's site-mapping key.</summary>
    [Required]
    [StringLength(PageKeyMaxLength, MinimumLength = 1)]
    public string PageKey { get; set; } = string.Empty;

    /// <summary>Active (non-idle, visible) seconds spent on the page.</summary>
    [Range(1, MaxActiveSeconds)]
    public int ActiveSeconds { get; set; }

    /// <summary>Prose words read during the interval.</summary>
    [Range(1, MaxWordsRead)]
    public int WordsRead { get; set; }

    /// <summary>Whether the reader reached the end of the page. Currently informational only.</summary>
    public bool Completed { get; set; }
}
