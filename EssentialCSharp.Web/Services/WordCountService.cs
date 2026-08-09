using HtmlAgilityPack;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Singleton service that computes prose-only word counts for all content pages
/// at startup and caches them in memory. Code blocks (<c>&lt;pre&gt;</c>,
/// <c>&lt;code&gt;</c>, <c>&lt;script&gt;</c>, <c>&lt;style&gt;</c>) are excluded
/// because they are read very differently from prose and would skew WPM estimates.
/// </summary>
public class WordCountService : IWordCountService
{
    // Tags whose text content is excluded from prose word counts.
    private static readonly HashSet<string> ExcludedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "pre", "code", "script", "style"
    };

    private readonly Dictionary<string, int> _pageWordCounts;
    private readonly Dictionary<int, int> _chapterWordCounts;
    private readonly int _bookWordCount;
    private readonly Dictionary<string, int> _wordsBeforePage;
    private readonly Dictionary<string, int> _chapterStartWords;
    private readonly IReadOnlyList<ChapterWordCount> _chapterWordCountList;

    public WordCountService(ISiteMappingService siteMappingService, IWebHostEnvironment hostingEnvironment)
    {
        _pageWordCounts = [];
        _chapterWordCounts = [];
        _wordsBeforePage = [];
        _chapterStartWords = [];

        // Walk mappings in canonical reading order: chapter → page → order-on-page.
        IEnumerable<SiteMapping> orderedMappings = siteMappingService.SiteMappings
            .OrderBy(m => m.ChapterNumber)
            .ThenBy(m => m.PageNumber)
            .ThenBy(m => m.OrderOnPage);

        int bookWords = 0;
        int currentChapter = -1;
        int chapterWords = 0;
        int chapterBookOffset = 0; // words before first page of current chapter in book

        foreach (SiteMapping mapping in orderedMappings)
        {
            string? pageKey = mapping.Keys.FirstOrDefault() ?? mapping.PrimaryKey;
            if (pageKey is null || _pageWordCounts.ContainsKey(pageKey))
            {
                // Multiple anchors on the same page; skip duplicates (already counted).
                continue;
            }

            // Chapter boundary: flush previous chapter totals.
            if (mapping.ChapterNumber != currentChapter)
            {
                if (currentChapter >= 0)
                {
                    _chapterWordCounts[currentChapter] = chapterWords;
                }
                currentChapter = mapping.ChapterNumber;
                chapterBookOffset = bookWords;
                chapterWords = 0;
            }

            int words = CountProseWords(hostingEnvironment.ContentRootPath, mapping.PagePath);
            _pageWordCounts[pageKey] = words;
            _wordsBeforePage[pageKey] = bookWords;
            _chapterStartWords[pageKey] = chapterWords;

            bookWords += words;
            chapterWords += words;
        }

        // Flush the last chapter.
        if (currentChapter >= 0)
        {
            _chapterWordCounts[currentChapter] = chapterWords;
        }

        _bookWordCount = bookWords;
        _chapterWordCountList = _chapterWordCounts
            .OrderBy(kv => kv.Key)
            .Select(kv => new ChapterWordCount(kv.Key, kv.Value))
            .ToList()
            .AsReadOnly();
    }

    public int GetPageWordCount(string pageKey) =>
        _pageWordCounts.TryGetValue(pageKey, out int count) ? count : 0;

    public int GetChapterWordCount(int chapterNumber) =>
        _chapterWordCounts.TryGetValue(chapterNumber, out int count) ? count : 0;

    public int GetBookWordCount() => _bookWordCount;

    public int GetWordsBeforePage(string pageKey) =>
        _wordsBeforePage.TryGetValue(pageKey, out int count) ? count : 0;

    public int GetChapterStartWords(string pageKey) =>
        _chapterStartWords.TryGetValue(pageKey, out int count) ? count : 0;

    public IReadOnlyList<ChapterWordCount> GetChapterWordCounts() => _chapterWordCountList;

    // ---

    private static int CountProseWords(string contentRoot, string[] pagePath)
    {
        string filePath = Path.Join(contentRoot, Path.Join(pagePath));
        if (!File.Exists(filePath))
        {
            return 0;
        }

        HtmlDocument doc = new();
        doc.Load(filePath);

        HtmlNode? body = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;

        // Remove excluded tags (modifies in-place on a clone isn't available, so remove from live tree).
        // XPath: //pre | //code | //script | //style
        string xpath = string.Join(" | ", ExcludedTags.Select(t => $"//{t}"));
        foreach (HtmlNode node in body.SelectNodes(xpath)?.ToList() ?? [])
        {
            node.Remove();
        }

        string text = body.InnerText;
        return CountWords(text);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        int count = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }
        return count;
    }
}
