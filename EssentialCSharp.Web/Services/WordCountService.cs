using System.Collections.Frozen;
using HtmlAgilityPack;
using Microsoft.Extensions.Hosting;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Computes prose-only word counts for all content pages when the host starts and caches
/// them in an immutable snapshot. <c>&lt;pre&gt;</c>, <c>&lt;script&gt;</c>, <c>&lt;style&gt;</c>
/// and generated <c>.code-block-section</c> listings are excluded because they are read very
/// differently from prose and would skew WPM estimates. Inline <c>&lt;code&gt;</c> inside prose
/// is counted, since readers do read those identifiers as part of the sentence.
/// </summary>
public sealed partial class WordCountService(
    ISiteMappingService siteMappingService,
    IWebHostEnvironment hostingEnvironment,
    ILogger<WordCountService> logger) : IWordCountService, IHostedLifecycleService
{
    private const string ExcludedXPath =
        "//pre | //script | //style | //*[contains(concat(' ', normalize-space(@class), ' '), ' code-block-section ')]";

    private volatile Snapshot? _snapshot;

    public bool IsReady => _snapshot is not null;

    public PageWordStats? GetPageStats(string pageKey) =>
        _snapshot?.Pages.GetValueOrDefault(pageKey);

    public int GetPageWordCount(string pageKey) => GetPageStats(pageKey)?.PageWords ?? 0;

    public int GetBookWordCount() => _snapshot?.BookWords ?? 0;

    /// <summary>
    /// Reads every mapped content page and publishes the word-count snapshot. A page that is
    /// missing or cannot be parsed is logged and counted as zero words; it never fails the build.
    /// </summary>
    public Task BuildAsync(CancellationToken cancellationToken = default)
    {
        // Walk mappings in canonical reading order: chapter -> page -> order-on-page.
        IEnumerable<SiteMapping> orderedMappings = siteMappingService.SiteMappings
            .OrderBy(m => m.ChapterNumber)
            .ThenBy(m => m.PageNumber)
            .ThenBy(m => m.OrderOnPage);

        // One entry per distinct file, in reading order. Later mappings of the same file only add keys.
        Dictionary<string, FileEntry> filesByPath = new(StringComparer.Ordinal);
        List<FileEntry> files = [];
        Dictionary<string, FileEntry> fileByKey = new(StringComparer.Ordinal);

        foreach (SiteMapping mapping in orderedMappings)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? path = ResolvePath(mapping);
            if (path is null)
            {
                LogPathEscapesRoot(logger, (mapping.Keys ?? []).FirstOrDefault() ?? mapping.PrimaryKey);
                continue;
            }

            if (!filesByPath.TryGetValue(path, out FileEntry? entry))
            {
                entry = new FileEntry(mapping.ChapterNumber, CountProseWords(path));
                filesByPath[path] = entry;
                files.Add(entry);
            }

            foreach (string key in (mapping.Keys ?? []).Append(mapping.PrimaryKey))
            {
                // First mapping wins if the sitemap contains a key collision.
                if (!string.IsNullOrWhiteSpace(key))
                {
                    fileByKey.TryAdd(key, entry);
                }
            }
        }

        Dictionary<int, int> chapterTotals = [];
        foreach (FileEntry file in files)
        {
            chapterTotals[file.Chapter] = chapterTotals.GetValueOrDefault(file.Chapter) + file.Words;
        }
        int bookWords = chapterTotals.Values.Sum();

        Dictionary<FileEntry, PageWordStats> statsByFile = new(ReferenceEqualityComparer.Instance);
        Dictionary<int, int> chapterRunning = [];
        int wordsBefore = 0;
        foreach (FileEntry file in files)
        {
            int chapterStart = chapterRunning.GetValueOrDefault(file.Chapter);
            statsByFile[file] = new PageWordStats(file.Words, chapterTotals[file.Chapter], wordsBefore, chapterStart, bookWords);
            chapterRunning[file.Chapter] = chapterStart + file.Words;
            wordsBefore += file.Words;
        }

        _snapshot = new Snapshot(
            fileByKey.ToFrozenDictionary(kv => kv.Key, kv => statsByFile[kv.Value], StringComparer.Ordinal),
            bookWords);

        return Task.CompletedTask;
    }

    // ---- Hosting: build before the host starts accepting requests ----

    public Task StartingAsync(CancellationToken cancellationToken) => BuildAsync(cancellationToken);
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ---- Implementation ----

    /// <summary>Returns a normalized, '/'-separated absolute path, or null if it leaves the content root.</summary>
    private string? ResolvePath(SiteMapping mapping)
    {
        string root = Path.GetFullPath(hostingEnvironment.ContentRootPath);
        string full = Path.GetFullPath(Path.Join(root, Path.Join(mapping.PagePath)));
        string relative = Path.GetRelativePath(root, full);
        return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
            ? null
            : full.Replace('\\', '/');
    }

    private int CountProseWords(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                LogFileMissing(logger, filePath);
                return 0;
            }

            HtmlDocument doc = new();
            doc.Load(filePath);

            HtmlNode root = doc.DocumentNode.SelectSingleNode("//body") ?? doc.DocumentNode;

            foreach (HtmlNode node in root.SelectNodes(ExcludedXPath)?.ToList() ?? [])
            {
                node.Remove();
            }

            return CountWords(HtmlEntity.DeEntitize(root.InnerText));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            LogFileUnreadable(logger, ex, filePath);
            return 0;
        }
    }

    private static int CountWords(string text)
    {
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Skipping word count for mapping '{Key}': page path escapes the content root.")]
    private static partial void LogPathEscapesRoot(ILogger logger, string? key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Content file '{FilePath}' not found; counting it as 0 words.")]
    private static partial void LogFileMissing(ILogger logger, string filePath);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read content file '{FilePath}'; counting it as 0 words.")]
    private static partial void LogFileUnreadable(ILogger logger, Exception exception, string filePath);

    private sealed record FileEntry(int Chapter, int Words);

    private sealed record Snapshot(
        FrozenDictionary<string, PageWordStats> Pages,
        int BookWords);
}
