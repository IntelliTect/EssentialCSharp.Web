using System.ComponentModel.DataAnnotations;

namespace EssentialCSharp.Web.Controllers;

public sealed class ReadingIntervalRequest
{
    public const int PageKeyMaxLength = 256;
    public const int MaxWordsRead = 100_000;

    /// <summary>The page's site-mapping key.</summary>
    [Required]
    [StringLength(PageKeyMaxLength, MinimumLength = 1)]
    public string PageKey { get; set; } = string.Empty;

    /// <summary>
    /// Active (non-idle, visible) seconds spent on the page. The upper bound is
    /// <c>ReadingOptions.MaxActiveSecondsPerInterval</c>, enforced by the controller.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ActiveSeconds { get; set; }

    /// <summary>Prose words read during the interval.</summary>
    [Range(1, MaxWordsRead)]
    public int WordsRead { get; set; }
}
