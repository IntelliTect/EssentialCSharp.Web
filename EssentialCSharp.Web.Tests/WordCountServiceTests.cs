using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace EssentialCSharp.Web.Tests;

public class WordCountServiceTests
{
    // ---- Helpers ----

    private static (string tempDir, string filePath) WriteTempHtmlFile(string html)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        string filePath = Path.Combine(tempDir, "page.html");
        File.WriteAllText(filePath, html);
        return (tempDir, filePath);
    }

    private static SiteMapping MakeMapping(string key, string contentRoot, string relativePath, int chapter, int page, int order = 1)
    {
        // PagePath is relative to contentRoot as individual path segments.
        string[] segments = relativePath.Split('/', '\\').Where(s => s.Length > 0).ToArray();
        return new SiteMapping(
            keys: [key],
            primaryKey: key,
            pagePath: segments,
            chapterNumber: chapter,
            pageNumber: page,
            orderOnPage: order,
            chapterTitle: $"Chapter {chapter}",
            rawHeading: key,
            anchorId: key,
            indentLevel: 0
        );
    }

    private static (WordCountService service, string tempDir) CreateService(IList<SiteMapping> mappings, string contentRoot)
    {
        Mock<ISiteMappingService> siteMappingMock = new();
        siteMappingMock.Setup(s => s.SiteMappings).Returns(mappings);

        Mock<IWebHostEnvironment> envMock = new();
        envMock.Setup(e => e.ContentRootPath).Returns(contentRoot);
        // IWebHostEnvironment also inherits IHostEnvironment, but WordCountService only uses ContentRootPath.

        var service = new WordCountService(siteMappingMock.Object, envMock.Object);
        return (service, contentRoot);
    }

    // ---- Tests ----

    [Test]
    public async Task GetPageWordCount_PlainProse_ReturnsCorrectCount()
    {
        // Arrange
        const string html = "<html><body><p>Hello world this is a test.</p></body></html>";
        (string tempDir, string filePath) = WriteTempHtmlFile(html);
        try
        {
            string relativePath = Path.GetFileName(filePath);
            SiteMapping mapping = MakeMapping("page1", tempDir, relativePath, 1, 1);
            (WordCountService service, _) = CreateService([mapping], tempDir);

            // Act
            int count = service.GetPageWordCount("page1");

            // Assert — "Hello world this is a test." = 6 words
            await Assert.That(count).IsEqualTo(6);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task GetPageWordCount_ExcludesPreAndCodeBlocks()
    {
        // Arrange — 3 prose words + code block that should be excluded
        const string html = """
            <html><body>
                <p>Hello world prose.</p>
                <pre>public static void Main() { var x = 1; }</pre>
                <code>Console.WriteLine(x);</code>
            </body></html>
            """;
        (string tempDir, string filePath) = WriteTempHtmlFile(html);
        try
        {
            string relativePath = Path.GetFileName(filePath);
            SiteMapping mapping = MakeMapping("page2", tempDir, relativePath, 1, 1);
            (WordCountService service, _) = CreateService([mapping], tempDir);

            // Act
            int count = service.GetPageWordCount("page2");

            // Assert — only "Hello world prose." = 3 words
            await Assert.That(count).IsEqualTo(3);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task GetPageWordCount_ExcludesScriptAndStyle()
    {
        const string html = """
            <html><head><style>body { margin: 0; }</style></head>
            <body>
                <p>Two words.</p>
                <script>var x = 1;</script>
            </body></html>
            """;
        (string tempDir, string filePath) = WriteTempHtmlFile(html);
        try
        {
            string relativePath = Path.GetFileName(filePath);
            SiteMapping mapping = MakeMapping("page3", tempDir, relativePath, 1, 1);
            (WordCountService service, _) = CreateService([mapping], tempDir);

            int count = service.GetPageWordCount("page3");

            // "Two words." = 2
            await Assert.That(count).IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task GetPageWordCount_FileNotFound_ReturnsZero()
    {
        // Arrange — map a page that doesn't have a corresponding file
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            SiteMapping mapping = MakeMapping("missing", tempDir, "nonexistent.html", 1, 1);
            (WordCountService service, _) = CreateService([mapping], tempDir);

            int count = service.GetPageWordCount("missing");

            await Assert.That(count).IsEqualTo(0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task GetChapterWordCount_SumsAllPagesInChapter()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Page A: 3 words; Page B: 4 words → chapter total 7
            File.WriteAllText(Path.Combine(tempDir, "a.html"), "<html><body><p>one two three</p></body></html>");
            File.WriteAllText(Path.Combine(tempDir, "b.html"), "<html><body><p>four five six seven</p></body></html>");

            SiteMapping[] mappings =
            [
                MakeMapping("a", tempDir, "a.html", 1, 1),
                MakeMapping("b", tempDir, "b.html", 1, 2),
            ];
            (WordCountService service, _) = CreateService(mappings, tempDir);

            await Assert.That(service.GetChapterWordCount(1)).IsEqualTo(7);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task GetBookWordCount_SumsAllChapters()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "ch1.html"), "<html><body><p>alpha beta</p></body></html>");      // 2 words
            File.WriteAllText(Path.Combine(tempDir, "ch2.html"), "<html><body><p>gamma delta epsilon</p></body></html>"); // 3 words

            SiteMapping[] mappings =
            [
                MakeMapping("ch1", tempDir, "ch1.html", 1, 1),
                MakeMapping("ch2", tempDir, "ch2.html", 2, 1),
            ];
            (WordCountService service, _) = CreateService(mappings, tempDir);

            await Assert.That(service.GetBookWordCount()).IsEqualTo(5);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public async Task GetWordsBeforePage_ReturnsCorrectPrefixSum()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "p1.html"), "<html><body><p>one two</p></body></html>"); // 2 words
            File.WriteAllText(Path.Combine(tempDir, "p2.html"), "<html><body><p>three</p></body></html>");   // 1 word

            SiteMapping[] mappings =
            [
                MakeMapping("p1", tempDir, "p1.html", 1, 1),
                MakeMapping("p2", tempDir, "p2.html", 1, 2),
            ];
            (WordCountService service, _) = CreateService(mappings, tempDir);

            await Assert.That(service.GetWordsBeforePage("p1")).IsEqualTo(0); // first page
            await Assert.That(service.GetWordsBeforePage("p2")).IsEqualTo(2); // 2 words from p1
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
