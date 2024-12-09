using EssentialCSharp.Web.Extensions;

namespace EssentialCSharp.Web.Tests;

public class SiteMappingTests
{
    static SiteMapping HelloWorldSiteMapping => new(
            key: "hello-world",
            pagePath:
            [
                "Chapters",
                "01",
                "Pages",
                "01.html"
            ],
            chapterNumber: 1,
            pageNumber: 1,
            chapterTitle: "Introducing C#",
            rawHeading: "Introduction",
            anchorId: "hello-world",
            indentLevel: 0
    );

    static SiteMapping CSyntaxFundamentalsSiteMapping => new(
            key: "c-syntax-fundamentals",
            pagePath:
            [
                "Chapters",
                "01",
                "Pages",
                "02.html"
            ],
            chapterNumber: 1,
            pageNumber: 2,
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

    [Fact]
    public void FindHelloWorldWithAnchorSlugReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = GetSiteMap().Find("hello-world#hello-world");
        Assert.NotNull(foundSiteMap);
        Assert.Equal(HelloWorldSiteMapping, foundSiteMap);
    }

    [Fact]
    public void FindCSyntaxFundamentalsWithSpacesReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = GetSiteMap().Find("C# Syntax Fundamentals");
        Assert.NotNull(foundSiteMap);
        Assert.Equal(CSyntaxFundamentalsSiteMapping, foundSiteMap);
    }

    [Fact]
    public void FindCSyntaxFundamentalsWithSpacesAndAnchorReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = GetSiteMap().Find("C# Syntax Fundamentals#hello-world");
        Assert.NotNull(foundSiteMap);
        Assert.Equal(CSyntaxFundamentalsSiteMapping, foundSiteMap);
    }

    [Fact]
    public void FindCSyntaxFundamentalsSanitizedWithAnchorReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = GetSiteMap().Find("c-syntax-fundamentals#hello-world");
        Assert.NotNull(foundSiteMap);
        Assert.Equal(CSyntaxFundamentalsSiteMapping, foundSiteMap);
    }

    [Fact]
    public void FindPercentComplete_KeyIsNull_ThrowsArgumentNullException()
    {
        // Arrange

        // Act
        string? percent = GetSiteMap().FindPercentComplete(null!);

        // Assert
        Assert.Null(percent);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void FindPercentComplete_KeyIsWhiteSpace_ThrowsArgumentException(string? key)
    {
        // Arrange

        // Act

        // Assert
        Assert.Throws<ArgumentException>(() =>
        {
            GetSiteMap().FindPercentComplete(key);
        });
    }

    [Theory]
    [InlineData("hello-world", "50.00")]
    [InlineData("c-syntax-fundamentals", "100.00")]
    public void FindPercentComplete_ValidKey_Success(string? key, string result)
    {
        // Arrange

        // Act
        string? percent = GetSiteMap().FindPercentComplete(key);

        // Assert
        Assert.Equal(result, percent);
    }

    [Fact]
    public void FindPercentComplete_EmptySiteMappings_ReturnsZeroPercent()
    {
        // Arrange
        IList<SiteMapping> siteMappings = new List<SiteMapping>();

        // Act
        string? percent = siteMappings.FindPercentComplete("test");

        // Assert
        Assert.Equal("0.00", percent);
    }
}
