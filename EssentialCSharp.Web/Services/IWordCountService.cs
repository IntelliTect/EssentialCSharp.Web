namespace EssentialCSharp.Web.Services;

/// <summary>
/// Provides pre-computed prose word counts for content pages, enabling
/// Kindle-style reading time estimates.
/// </summary>
/// <remarks>
/// The cache is built when the host starts. Until then (<see cref="IsReady"/> is <see langword="false"/>)
/// every query returns "no data": <see cref="GetPageStats"/> returns <see langword="null"/>,
/// counts are 0.
/// </remarks>
public interface IWordCountService
{
    /// <summary>Gets whether the word-count cache has been built.</summary>
    bool IsReady { get; }

    /// <summary>
    /// Gets all word statistics for a page key, or <see langword="null"/> if the key is unknown
    /// (or the cache is not ready yet). A known key whose content file is missing or unreadable
    /// yields stats with zero page words.
    /// </summary>
    PageWordStats? GetPageStats(string pageKey);

    /// <summary>Gets the prose word count for a specific page key (0 if unknown).</summary>
    int GetPageWordCount(string pageKey);

    /// <summary>Gets the total prose word count for the entire book.</summary>
    int GetBookWordCount();
}

/// <summary>Immutable prose word statistics for one page.</summary>
/// <param name="PageWords">Prose words on the page.</param>
/// <param name="ChapterWords">Prose words in the page's whole chapter.</param>
/// <param name="WordsBeforePage">Prose words on all pages before this one across the book.</param>
/// <param name="ChapterStartWords">Prose words on pages before this one within its chapter.</param>
/// <param name="BookWords">Prose words in the whole book.</param>
public sealed record PageWordStats(
    int PageWords,
    int ChapterWords,
    int WordsBeforePage,
    int ChapterStartWords,
    int BookWords);
