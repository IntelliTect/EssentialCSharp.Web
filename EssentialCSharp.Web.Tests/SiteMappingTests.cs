using EssentialCSharp.Web.Extensions;

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
    public async Task Find_NullSiteMappings_ThrowsArgumentNullException()
    {
        await Assert.That(() => ((IList<SiteMapping>)null!).Find("hello-world"))
            .Throws<ArgumentNullException>();
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
    public async Task FindPercentComplete_NullSiteMappings_ThrowsArgumentNullException()
    {
        await Assert.That(() => ((IList<SiteMapping>)null!).FindPercentComplete("hello-world"))
            .Throws<ArgumentNullException>();
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
    public async Task FindPercentComplete_MappingsWithoutPrimaryKey_MatchOnKeys()
    {
        // The placeholder sitemap.json used by CI and local dev has no PrimaryKey.
        IList<SiteMapping> siteMappings =
        [
            new(keys: ["first"], primaryKey: null!, pagePath: ["a.html"], chapterNumber: 1, pageNumber: 1, orderOnPage: 1,
                chapterTitle: "C", rawHeading: "First", anchorId: "first", indentLevel: 0),
            new(keys: ["second"], primaryKey: null!, pagePath: ["b.html"], chapterNumber: 1, pageNumber: 2, orderOnPage: 1,
                chapterTitle: "C", rawHeading: "Second", anchorId: "second", indentLevel: 0),
        ];

        string? percent = siteMappings.FindPercentComplete("first");

        await Assert.That(percent).IsEqualTo("50.00");
    }

    private static SiteMapping MappingWithKeys(string[] keys, string? primaryKey) =>
        new(keys: [.. keys], primaryKey: primaryKey!, pagePath: ["a.html"], chapterNumber: 1, pageNumber: 1, orderOnPage: 1,
            chapterTitle: "C", rawHeading: "Heading", anchorId: "heading", indentLevel: 0);

    [Test]
    [Arguments(new[] { "first", "alias" }, "primary", "first")]
    [Arguments(new[] { "first" }, null, "first")] // the placeholder sitemap.json has no PrimaryKey
    [Arguments(new string[0], "primary", "primary")]
    [Arguments(new[] { " ", "second" }, "primary", "second")]
    public async Task GetPageKey_MappingWithAUsableKey_ReturnsIt(string[] keys, string? primaryKey, string expected)
    {
        await Assert.That(MappingWithKeys(keys, primaryKey).GetPageKey()).IsEqualTo(expected);
    }

    [Test]
    [Arguments(new string[0], null)]
    [Arguments(new string[0], "")]
    [Arguments(new[] { "", "  " }, " ")]
    public async Task GetPageKey_MappingWithoutAUsableKey_IsNullSoTheLayoutStillRendersWithoutProgress(string[] keys, string? primaryKey)
    {
        // The layout passes the page key straight to FindPercentComplete, which rejects a blank key; a null key must
        // instead mean "no progress for this page" rather than breaking the page.
        string? pageKey = MappingWithKeys(keys, primaryKey).GetPageKey();

        using (Assert.Multiple())
        {
            await Assert.That(pageKey).IsNull();
            await Assert.That(GetSiteMap().FindPercentComplete(pageKey)).IsNull();
        }
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
}