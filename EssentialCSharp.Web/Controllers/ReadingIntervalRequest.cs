using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Controllers;

public sealed class ReadingIntervalRequest : IValidatableObject
{
    public const int PageKeyMaxLength = 256;
    public const int MaxWordsRead = 100_000;

    /// <summary>The page's site-mapping key.</summary>
    [Required]
    [StringLength(PageKeyMaxLength, MinimumLength = 1)]
    public string PageKey { get; set; } = string.Empty;

    /// <summary>
    /// Active (non-idle, visible) seconds spent on the page. The upper bound is
    /// <c>ReadingOptions.MaxActiveSecondsPerInterval</c>, enforced in <see cref="Validate"/>.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int ActiveSeconds { get; set; }

    /// <summary>Prose words read during the interval.</summary>
    [Range(1, MaxWordsRead)]
    public int WordsRead { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        int maxActiveSeconds = validationContext.GetRequiredService<IOptions<ReadingOptions>>().Value.MaxActiveSecondsPerInterval;
        if (ActiveSeconds > maxActiveSeconds)
        {
            yield return new ValidationResult(
                $"An interval may contain at most {maxActiveSeconds} active seconds.",
                [nameof(ActiveSeconds)]);
        }
    }
}
