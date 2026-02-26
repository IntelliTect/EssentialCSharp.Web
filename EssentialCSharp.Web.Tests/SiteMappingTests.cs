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
}