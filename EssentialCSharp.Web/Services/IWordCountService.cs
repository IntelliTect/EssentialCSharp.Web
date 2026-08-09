namespace EssentialCSharp.Web.Services;

/// <summary>
/// Provides pre-computed prose word counts for content pages, enabling
/// Kindle-style reading time estimates.
/// </summary>
public interface IWordCountService
{
    /// <summary>Gets the prose word count for a specific page key.</summary>
    int GetPageWordCount(string pageKey);

    /// <summary>Gets the total prose word count for a chapter.</summary>
    int GetChapterWordCount(int chapterNumber);

    /// <summary>Gets the total prose word count for the entire book.</summary>
    int GetBookWordCount();

    /// <summary>
    /// Gets the number of prose words before the given page (across the entire book).
    /// Used for "words remaining in book" math.
    /// </summary>
    int GetWordsBeforePage(string pageKey);

    /// <summary>
    /// Gets the number of prose words before the given page within its chapter.
    /// Used for "words remaining in chapter" math.
    /// </summary>
    int GetChapterStartWords(string pageKey);

    /// <summary>Gets per-chapter word count summary for the book-stats API response.</summary>
    IReadOnlyList<ChapterWordCount> GetChapterWordCounts();
}

/// <summary>Per-chapter word count summary returned by the book-stats API.</summary>
public record ChapterWordCount(int ChapterNumber, int WordCount);
