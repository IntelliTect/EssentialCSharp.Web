using System.Globalization;
using System.Text.Json;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Moq;

namespace EssentialCSharp.Web.Tests;

public class SiteMappingTests
{
    static SiteMapping HelloWorldSiteMapping => new(
            keys: ["hello-world"],
            primaryKey: "hello-world",
            pagePath:
            [
                "Chapters",
                "01",
                "Pages",
                "01.html"
            ],
            chapterNumber: 1,
            pageNumber: 1,
            orderOnPage: 1,
            chapterTitle: "Introducing C#",
            rawHeading: "Introduction",
            anchorId: "hello-world",
            indentLevel: 0
    );

    static SiteMapping CSyntaxFundamentalsSiteMapping => new(
            keys: ["c-syntax-fundamentals"],
            primaryKey: "c-syntax-fundamentals",
            pagePath:
            [
                "Chapters",
                "01",
                "Pages",
                "02.html"
            ],
            chapterNumber: 1,
            pageNumber: 2,
            orderOnPage: 1,
            chapterTitle: "Introducing C#",
            rawHeading: "C# Syntax Fundamentals",
            anchorId: "c-syntax-fundamentals",
            indentLevel: 2
    );

    public static List<SiteMapping> GetSiteMap()
    {
        return
        [
            HelloWorldSiteMapping,
            CSyntaxFundamentalsSiteMapping
        ];
    }

    [Test]
    public async Task FindHelloWorldWithAnchorSlugReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = GetSiteMap().Find("hello-world#hello-world");
        await Assert.That(foundSiteMap).IsNotNull();
        await Assert.That(foundSiteMap).IsEquivalentTo(HelloWorldSiteMapping);
    }

    [Test]
    public async Task FindCSyntaxFundamentalsWithSpacesReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = GetSiteMap().Find("C# Syntax Fundamentals");
        await Assert.That(foundSiteMap).IsNotNull();
        await Assert.That(foundSiteMap).IsEquivalentTo(CSyntaxFundamentalsSiteMapping);
    }

    [Test]
    public async Task FindCSyntaxFundamentalsWithSpacesAndAnchorReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = GetSiteMap().Find("C# Syntax Fundamentals#hello-world");
        await Assert.That(foundSiteMap).IsNotNull();
        await Assert.That(foundSiteMap).IsEquivalentTo(CSyntaxFundamentalsSiteMapping);
    }

    [Test]
    public async Task FindCSyntaxFundamentalsSanitizedWithAnchorReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = GetSiteMap().Find("c-syntax-fundamentals#hello-world");
        await Assert.That(foundSiteMap).IsNotNull();
        await Assert.That(foundSiteMap).IsEquivalentTo(CSyntaxFundamentalsSiteMapping);
    }

    [Test]
    public async Task FindPercentComplete_KeyIsNull_ReturnsNull()
    {
        // Arrange

        // Act
        string? percent = GetSiteMap().FindPercentComplete(null!);

        // Assert
        await Assert.That(percent).IsNull();
    }

    [Test]
    [Arguments("   ")]
    [Arguments("")]
    public async Task FindPercentComplete_KeyIsWhiteSpace_ThrowsArgumentException(string? key)
    {
        // Arrange

        // Act

        // Assert
        await Assert.That(() => GetSiteMap().FindPercentComplete(key)).Throws<ArgumentException>();
    }

    [Test]
    [Arguments("hello-world", "50.00")]
    [Arguments("c-syntax-fundamentals", "100.00")]
    public async Task FindPercentComplete_ValidKey_Success(string? key, string result)
    {
        // Arrange

        // Act
        string? percent = GetSiteMap().FindPercentComplete(key);

        // Assert
        await Assert.That(percent).IsEqualTo(result);
    }

    [Test]
    public async Task FindPercentComplete_EmptySiteMappings_ReturnsZeroPercent()
    {
        // Arrange
        IList<SiteMapping> siteMappings = new List<SiteMapping>();

        // Act
        string? percent = siteMappings.FindPercentComplete("test");

        // Assert
        await Assert.That(percent).IsEqualTo("0.00");
    }

    [Test]
    public async Task FindPercentComplete_KeyNotFound_ReturnsZeroPercent()
    {
        // Arrange

        // Act
        string? percent = GetSiteMap().FindPercentComplete("non-existent-key");

        // Assert
        await Assert.That(percent).IsEqualTo("0.00");
    }

    [Test]
    public async Task GetTocData_OrdersChaptersAndBuildsNestedItems()
    {
        // Arrange
        string contentRoot = Path.Combine(Path.GetTempPath(), $"essential-csharp-{Guid.NewGuid():N}");
        string chaptersDirectory = Path.Combine(contentRoot, "Chapters");
        Directory.CreateDirectory(chaptersDirectory);

        List<SiteMapping> siteMappings =
        [
            CreateSiteMapping(2, 1, 1, 0, "chapter-two", "Chapter Two"),
            CreateSiteMapping(1, 2, 1, 0, "chapter-one-page-two", "Page Two"),
            CreateSiteMapping(1, 1, 1, 0, "chapter-one", "Chapter One"),
            CreateSiteMapping(1, 1, 2, 1, "chapter-one-topic", "Topic"),
            CreateSiteMapping(1, 1, 3, 2, "chapter-one-subtopic", "Subtopic"),
            CreateSiteMapping(1, 1, 4, 1, "chapter-one-second-topic", "Second Topic")
        ];

        await File.WriteAllTextAsync(
            Path.Combine(chaptersDirectory, "sitemap.json"),
            JsonSerializer.Serialize(siteMappings));

        Mock<IWebHostEnvironment> environment = new();
        environment.SetupGet(x => x.ContentRootPath).Returns(contentRoot);

        try
        {
            // Act
            SiteMappingService service = new(environment.Object);
            List<SiteMappingDto> chapters = service.GetTocData().ToList();

            // Assert
            using (Assert.Multiple())
            {
                await Assert.That(chapters.Select(x => x.Key).ToArray()).IsEqualTo(["chapter-one", "chapter-two"]);
                await Assert.That(chapters[0].Title).IsEqualTo("Chapter 1: Chapter One");
                await Assert.That(chapters[0].Items.Select(x => x.Key).ToArray())
                    .IsEqualTo(["chapter-one-topic", "chapter-one-second-topic"]);
                await Assert.That(chapters[0].Items.First().Items.Select(x => x.Key).ToArray())
                    .IsEqualTo(["chapter-one-subtopic"]);
                await Assert.That(chapters[1].Title).IsEqualTo("Chapter 2: Chapter Two");
            }
        }
        finally
        {
            Directory.Delete(contentRoot, recursive: true);
        }
    }

    private static SiteMapping CreateSiteMapping(
        int chapterNumber,
        int pageNumber,
        int orderOnPage,
        int indentLevel,
        string key,
        string heading) =>
        new(
            keys: [key],
            primaryKey: key,
            pagePath:
            [
                "Chapters",
                chapterNumber.ToString("D2", CultureInfo.InvariantCulture),
                "Pages",
                $"{pageNumber:D2}.html"
            ],
            chapterNumber: chapterNumber,
            pageNumber: pageNumber,
            orderOnPage: orderOnPage,
            chapterTitle: heading,
            rawHeading: heading,
            anchorId: key,
            indentLevel: indentLevel);
}