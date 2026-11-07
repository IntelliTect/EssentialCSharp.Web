using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using OS = TUnit.Core.Enums.OS;

namespace EssentialCSharp.Web.Tests;

public class WordCountServiceTests
{
    // Three prose words used as a constant baseline next to whatever is being included or excluded.
    private const string Baseline = "<p>alpha beta gamma</p>";
    private const int BaselineWords = 3;

    [Test]
    public async Task PlainProse_IsCountedByWhitespaceSeparatedWords()
    {
        using TempContentDirectory dir = new();
        dir.Write("page.html", "<html><body><p>Hello world,   this\nis a test.</p></body></html>");

        WordCountService service = await dir.BuildAsync(Map("page", "page.html", 1, 1));

        await Assert.That(service.GetPageWordCount("page")).IsEqualTo(6);
    }

    [Test]
    [Arguments("<pre>public static void Main() { var x = 1; }</pre>")]
    [Arguments("<pre><code>Console.WriteLine(x);</code></pre>")]
    [Arguments("<script>var x = 1;</script>")]
    [Arguments("<style>body { margin: 0; }</style>")]
    [Arguments("<div class=\"element-section code-block-section\"><div class=\"code-block-heading\">Listing 1.16: Using it</div><div class=\"code-line\">public&nbsp;class HeyYou</div></div>")]
    [Arguments("<div><section><pre>deeply nested code here</pre></section></div>")]
    [Arguments("<div class=\"code-block-section\"><pre>pre inside a generated listing</pre><script>x()</script></div>")]
    [Arguments("<div class=\"code-block-section\"><div class=\"code-block-section\">inner listing words</div>outer listing words</div>")]
    [Arguments("<PRE>UPPERCASE TAG LISTING</PRE>")]
    [Arguments("<SCRIPT>var upper = 1;</SCRIPT>")]
    [Arguments("<!-- a comment holding several words -->")]
    public async Task CodeListingsAndNonProseTags_DoNotAddToTheCount(string excludedHtml)
    {
        using TempContentDirectory dir = new();
        dir.Write("page.html", $"<html><head><style>h1 {{ color: red; }}</style></head><body>\n{Baseline}\n{excludedHtml}\n{Baseline}\n</body></html>");

        WordCountService service = await dir.BuildAsync(Map("page", "page.html", 1, 1));

        await Assert.That(service.GetPageWordCount("page")).IsEqualTo(2 * BaselineWords);
    }

    [Test]
    public async Task UnclosedPreAtEndOfPage_ExcludesItsContent()
    {
        using TempContentDirectory dir = new();
        dir.Write("page.html", $"<html><body>{Baseline}<pre>unclosed listing words here</body></html>");

        WordCountService service = await dir.BuildAsync(Map("page", "page.html", 1, 1));

        await Assert.That(service.GetPageWordCount("page")).IsEqualTo(BaselineWords);
    }

    [Test]
    public async Task InlineCodeInProse_IsCounted()
    {
        using TempContentDirectory dir = new();
        dir.Write("page.html", "<html><body><p>Call <code>Console.WriteLine</code> now</p></body></html>");

        WordCountService service = await dir.BuildAsync(Map("page", "page.html", 1, 1));

        await Assert.That(service.GetPageWordCount("page")).IsEqualTo(3);
    }

    [Test]
    public async Task HtmlEntities_AreDecodedBeforeCounting()
    {
        using TempContentDirectory dir = new();
        // Requirement: entities are decoded and the decoded text is counted like any other prose. Decoded this reads
        // "a b AT&T List<T>": a non-breaking space separates two words, while "AT&T" and "List<T>" are one word each
        // (undecoded, "a&nbsp;b" would be a single word and the total would be 3).
        dir.Write("page.html", "<html><body><p>a&nbsp;b AT&amp;T List&lt;T&gt;</p></body></html>");

        WordCountService service = await dir.BuildAsync(Map("page", "page.html", 1, 1));

        await Assert.That(service.GetPageWordCount("page")).IsEqualTo(4);
    }

    [Test]
    [Arguments("<html><body></body></html>", 0)]
    [Arguments("", 0)]
    [Arguments("<html><body>   \n  </body></html>", 0)]
    [Arguments("<p>one two</p>", 2)] // fragment without <html>/<body>
    public async Task EmptyOrBodylessContent_IsCountedWithoutFailing(string html, int expected)
    {
        using TempContentDirectory dir = new();
        dir.Write("page.html", html);

        WordCountService service = await dir.BuildAsync(Map("page", "page.html", 1, 1));

        await Assert.That(service.GetPageWordCount("page")).IsEqualTo(expected);
    }

    [Test]
    public async Task UnknownKey_HasNoStats()
    {
        using TempContentDirectory dir = new();
        dir.Write("present.html", $"<body>{Baseline}</body>");

        WordCountService service = await dir.BuildAsync(Map("present", "present.html", 1, 1));

        using (Assert.Multiple())
        {
            await Assert.That(service.GetPageStats("not-a-key")).IsNull();
            await Assert.That(service.GetPageWordCount("not-a-key")).IsEqualTo(0);
            await Assert.That(service.GetPageWordCount("present")).IsEqualTo(BaselineWords);
        }
    }

    [Test]
    public async Task MappedKeyWithMissingFile_CountsZeroKeepsStatsAndLogsAWarning()
    {
        using TempContentDirectory dir = new();
        dir.Write("present.html", $"<body>{Baseline}</body>");
        CapturingLogger<WordCountService> logger = new();

        WordCountService service = await dir.BuildAsync(
            logger,
            Map("present", "present.html", 1, 1),
            Map("missing", "nonexistent.html", 1, 2));

        using (Assert.Multiple())
        {
            await Assert.That(service.GetPageStats("missing")).IsNotNull();
            await Assert.That(service.GetPageWordCount("missing")).IsEqualTo(0);
            await Assert.That(service.GetPageWordCount("present")).IsEqualTo(BaselineWords);
            await Assert.That(logger.Warnings).HasSingleItem().And.Contains(w => w.Mentions("nonexistent.html"));
        }
    }

    // FileShare.None makes the page read fail with an IOException on Windows. Other platforms use advisory locks,
    // so the read would succeed there and the failure cannot be provoked.
    [Test]
    [RunOn(OS.Windows)]
    public async Task FileThatCannotBeRead_IsLoggedCountsZeroAndDoesNotAffectOtherPages()
    {
        using TempContentDirectory dir = new();
        dir.Write("a.html", $"<body>{Baseline}</body>");
        dir.Write("locked.html", $"<body>{Baseline}</body>");
        dir.Write("c.html", $"<body>{Baseline}</body>");
        CapturingLogger<WordCountService> logger = new();

        using FileStream exclusive = new(dir.PathOf("locked.html"), FileMode.Open, FileAccess.Read, FileShare.None);
        WordCountService service = await dir.BuildAsync(
            logger,
            Map("a", "a.html", 1, 1),
            Map("locked", "locked.html", 1, 2),
            Map("c", "c.html", 1, 3));

        using (Assert.Multiple())
        {
            await Assert.That(service.GetPageWordCount("a")).IsEqualTo(BaselineWords);
            await Assert.That(service.GetPageWordCount("locked")).IsEqualTo(0);
            await Assert.That(service.GetPageWordCount("c")).IsEqualTo(BaselineWords);
            await Assert.That(logger.Warnings).HasSingleItem().And.Contains(w => w.Mentions("locked.html"));
        }
    }

    [Test]
    public async Task MappedPathThatIsADirectory_IsTreatedAsMissingLoggedAndDoesNotAffectOtherPages()
    {
        using TempContentDirectory dir = new();
        dir.Write("a.html", $"<body>{Baseline}</body>");
        dir.Write("c.html", $"<body>{Baseline}</body>");
        // A directory where a page file should be. File.Exists is false for it, so this covers the missing/non-file path, not the read-failure catch.
        Directory.CreateDirectory(dir.PathOf("locked.html"));
        CapturingLogger<WordCountService> logger = new();

        WordCountService service = await dir.BuildAsync(
            logger,
            Map("a", "a.html", 1, 1),
            Map("locked", "locked.html", 1, 2),
            Map("c", "c.html", 1, 3));

        using (Assert.Multiple())
        {
            await Assert.That(service.GetPageWordCount("a")).IsEqualTo(BaselineWords);
            await Assert.That(service.GetPageWordCount("locked")).IsEqualTo(0);
            await Assert.That(service.GetPageWordCount("c")).IsEqualTo(BaselineWords);
            await Assert.That(service.GetPageStats("c")?.WordsBeforePage).IsEqualTo(BaselineWords);
            await Assert.That(logger.Warnings).HasSingleItem().And.Contains(w => w.Mentions("locked.html"));
        }
    }

    private const string AbsolutePathToken = "{absolute path of the outside file}";

    [Test]
    [Arguments("../outside.html")]
    [Arguments("..\\outside.html")]
    [Arguments("sub/../../outside.html")]
    [Arguments("sub\\..\\..\\outside.html")]
    [Arguments("../content-evil/outside.html")] // sibling directory sharing the content root's name as a prefix
    [Arguments("..\\content-evil\\outside.html")]
    [Arguments(AbsolutePathToken)]
    public async Task PagePathLeadingOutsideTheContentRoot_IsNeverCountedAndIsLogged(string pagePath)
    {
        using TempContentDirectory dir = new();
        dir.Write("ok.html", $"<body>{Baseline}</body>");
        // A real, readable file outside the content root with distinctive words: if it were read, the count would show it.
        dir.WriteOutside("outside.html", "<body><p>outside words must never be counted</p></body>");
        dir.WriteOutside("content-evil/outside.html", "<body><p>outside words must never be counted</p></body>");
        string resolvedPath = pagePath == AbsolutePathToken ? dir.OutsidePathOf("outside.html") : pagePath;
        CapturingLogger<WordCountService> logger = new();

        WordCountService service = await dir.BuildAsync(
            logger,
            Map("ok", "ok.html", 1, 1),
            Map("escape", resolvedPath, 1, 2));

        using (Assert.Multiple())
        {
            await Assert.That(service.GetPageWordCount("escape")).IsEqualTo(0);
            await Assert.That(service.GetPageWordCount("ok")).IsEqualTo(BaselineWords);
            await Assert.That(service.GetBookWordCount()).IsEqualTo(BaselineWords);
            await Assert.That(logger.Warnings).IsNotEmpty().Because("a skipped mapping must not be silent");
        }
    }

    [Test]
    public async Task PageStats_ReportPageChapterAndBookOffsets()
    {
        using TempContentDirectory dir = new();
        dir.Write("a.html", "<body><p>one two</p></body>");        // 2
        dir.Write("b.html", "<body><p>three</p></body>");          // 1
        dir.Write("c.html", "<body><p>four five six</p></body>");  // 3 (chapter 2)

        WordCountService service = await dir.BuildAsync(
            Map("a", "a.html", 1, 1),
            Map("b", "b.html", 1, 2),
            Map("c", "c.html", 2, 1));

        using (Assert.Multiple())
        {
            await Assert.That(service.GetPageStats("a")).IsEqualTo(new PageWordStats(2, 3, 0, 0, 6));
            await Assert.That(service.GetPageStats("b")).IsEqualTo(new PageWordStats(1, 3, 2, 2, 6));
            await Assert.That(service.GetPageStats("c")).IsEqualTo(new PageWordStats(3, 3, 3, 0, 6));
            await Assert.That(service.GetBookWordCount()).IsEqualTo(6);
        }
    }

    [Test]
    public async Task MappingsInAnyInputOrder_YieldReadingOrderOffsets()
    {
        using TempContentDirectory dir = new();
        dir.Write("a.html", "<body><p>one two</p></body>");
        dir.Write("b.html", "<body><p>three</p></body>");
        dir.Write("c.html", "<body><p>four five six</p></body>");

        WordCountService service = await dir.BuildAsync(
            Map("c", "c.html", 2, 1),
            Map("b", "b.html", 1, 2),
            Map("a", "a.html", 1, 1));

        using (Assert.Multiple())
        {
            await Assert.That(service.GetPageStats("a")?.WordsBeforePage).IsEqualTo(0);
            await Assert.That(service.GetPageStats("b")?.WordsBeforePage).IsEqualTo(2);
            await Assert.That(service.GetPageStats("b")?.ChapterStartWords).IsEqualTo(2);
            await Assert.That(service.GetPageStats("c")?.WordsBeforePage).IsEqualTo(3);
            await Assert.That(service.GetPageStats("c")?.ChapterStartWords).IsEqualTo(0);
        }
    }

    [Test]
    public async Task SeveralMappingsOfOneFile_CountOnceAndShareStatsAcrossAllKeys()
    {
        using TempContentDirectory dir = new();
        dir.Write("a.html", "<body><p>one two three</p></body>");
        dir.Write("b.html", "<body><p>four five</p></body>");

        WordCountService service = await dir.BuildAsync(
            Map("page-one", "a.html", 1, 1, keys: ["page-one", "page-one-alias"]),
            Map("page-one-heading", "./a.html", 1, 1, order: 2),
            Map("page-two", "b.html", 1, 2));

        PageWordStats expectedFirst = new(3, 5, 0, 0, 5);
        using (Assert.Multiple())
        {
            await Assert.That(service.GetPageStats("page-one")).IsEqualTo(expectedFirst);
            await Assert.That(service.GetPageStats("page-one-alias")).IsEqualTo(expectedFirst);
            await Assert.That(service.GetPageStats("page-one-heading")).IsEqualTo(expectedFirst);
            await Assert.That(service.GetPageStats("page-two")).IsEqualTo(new PageWordStats(2, 5, 3, 3, 5));
            await Assert.That(service.GetBookWordCount()).IsEqualTo(5);
        }
    }

    [Test]
    public async Task KeyCollision_FirstMappingInReadingOrderWins()
    {
        using TempContentDirectory dir = new();
        dir.Write("first.html", "<body><p>one two</p></body>");
        dir.Write("second.html", "<body><p>three four five</p></body>");

        WordCountService service = await dir.BuildAsync(
            Map("shared", "second.html", 1, 2),
            Map("shared", "first.html", 1, 1));

        await Assert.That(service.GetPageWordCount("shared")).IsEqualTo(2);
    }

    [Test]
    public async Task BeforeCacheIsBuilt_QueriesReturnNoData()
    {
        using TempContentDirectory dir = new();
        dir.Write("a.html", $"<body>{Baseline}</body>");

        WordCountService service = dir.CreateService(NullLogger<WordCountService>.Instance, Map("a", "a.html", 1, 1));

        using (Assert.Multiple())
        {
            await Assert.That(service.GetPageStats("a")).IsNull();
            await Assert.That(service.GetPageWordCount("a")).IsEqualTo(0);
            await Assert.That(service.GetBookWordCount()).IsEqualTo(0);
        }
    }

    [Test]
    public async Task HostStartup_BuildsTheCacheBeforeRequestsAreServed()
    {
        using TempContentDirectory dir = new();
        dir.Write("a.html", $"<body>{Baseline}</body>");
        WordCountService service = dir.CreateService(NullLogger<WordCountService>.Instance, Map("a", "a.html", 1, 1));

        await service.StartingAsync(CancellationToken.None);

        using (Assert.Multiple())
        {
            await Assert.That(service.GetPageWordCount("a")).IsEqualTo(BaselineWords);
        }
    }

    [Test]
    public async Task Build_HonorsCancellation()
    {
        using TempContentDirectory dir = new();
        dir.Write("a.html", $"<body>{Baseline}</body>");
        WordCountService service = dir.CreateService(NullLogger<WordCountService>.Instance, Map("a", "a.html", 1, 1));
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        await Assert.That(async () => await service.BuildAsync(cts.Token)).Throws<OperationCanceledException>();
    }

    // ---- Helpers ----

    private static SiteMapping Map(string key, string relativePath, int chapter, int page, int order = 1, string[]? keys = null) =>
        new(
            keys: [.. keys ?? [key]],
            primaryKey: key,
            pagePath: relativePath.Split('/', '\\').Where(s => s.Length > 0).ToArray(),
            chapterNumber: chapter,
            pageNumber: page,
            orderOnPage: order,
            chapterTitle: $"Chapter {chapter}",
            rawHeading: key,
            anchorId: key,
            indentLevel: 0);

    /// <summary>A throwaway content root that deletes itself when disposed.</summary>
    private sealed class TempContentDirectory : IDisposable
    {
        // Content lives one level below the temp root so "../outside.html" stays inside a directory we own.
        private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        private readonly string _contentRoot;

        public TempContentDirectory()
        {
            _contentRoot = Path.Combine(_root, "content");
            Directory.CreateDirectory(_contentRoot);
        }

        public string PathOf(string relativePath) => Path.Combine(_contentRoot, relativePath);

        public void Write(string relativePath, string html) => File.WriteAllText(PathOf(relativePath), html);

        /// <summary>The same relative path resolved against the temp root, i.e. outside (beside) the content root.</summary>
        public string OutsidePathOf(string relativePath) => Path.Combine(_root, relativePath);

        public void WriteOutside(string relativePath, string html)
        {
            string path = OutsidePathOf(relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, html);
        }

        public WordCountService CreateService(ILogger<WordCountService> logger, params SiteMapping[] mappings)
        {
            Mock<ISiteMappingService> siteMappings = new();
            siteMappings.Setup(s => s.SiteMappings).Returns(mappings);
            Mock<IWebHostEnvironment> environment = new();
            environment.Setup(e => e.ContentRootPath).Returns(_contentRoot);
            return new WordCountService(siteMappings.Object, environment.Object, logger);
        }

        public Task<WordCountService> BuildAsync(params SiteMapping[] mappings) =>
            BuildAsync(NullLogger<WordCountService>.Instance, mappings);

        public async Task<WordCountService> BuildAsync(ILogger<WordCountService> logger, params SiteMapping[] mappings)
        {
            WordCountService service = CreateService(logger, mappings);
            await service.BuildAsync();
            return service;
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }

    private sealed record LogEntry(LogLevel Level, string Message, IReadOnlyList<KeyValuePair<string, object?>> Properties)
    {
        /// <summary>True when the message or any structured property value contains <paramref name="text"/>.</summary>
        public bool Mentions(string text) =>
            Message.Contains(text, StringComparison.Ordinal)
            || Properties.Any(p => p.Value?.ToString()?.Contains(text, StringComparison.Ordinal) == true);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IEnumerable<LogEntry> Warnings => Entries.Where(e => e.Level == LogLevel.Warning);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            IReadOnlyList<KeyValuePair<string, object?>> properties =
                state as IReadOnlyList<KeyValuePair<string, object?>> ?? [];
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), properties));
        }
    }
}
