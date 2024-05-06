using EssentialCSharp.Web.Extensions;

namespace EssentialCSharp.Web.Tests;

public class SiteMappingTests
{
    static SiteMapping HelloWorldSiteMapping { get; } = new(
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

    static SiteMapping CSyntaxFundamentalsSiteMapping { get; } = new(
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
        Assert.Equal(CSyntaxFundamentalsSiteMapping.Key, foundSiteMap.Key);
        Assert.Equal(CSyntaxFundamentalsSiteMapping.PagePath, foundSiteMap.PagePath);
        Assert.Equal(CSyntaxFundamentalsSiteMapping.RawHeading, foundSiteMap.RawHeading);
        Assert.Equal(CSyntaxFundamentalsSiteMapping.ChapterTitle, foundSiteMap.ChapterTitle);
        Assert.Equal(CSyntaxFundamentalsSiteMapping.ChapterNumber, foundSiteMap.ChapterNumber);
        Assert.Equal(CSyntaxFundamentalsSiteMapping.PageNumber, foundSiteMap.PageNumber);
        Assert.Equal(CSyntaxFundamentalsSiteMapping.IndentLevel, foundSiteMap.IndentLevel);
        Assert.Equal(CSyntaxFundamentalsSiteMapping.AnchorId, foundSiteMap.AnchorId);
        Assert.Equal(CSyntaxFundamentalsSiteMapping.IncludeInSitemap, foundSiteMap.IncludeInSitemap);
        Assert.Equal(CSyntaxFundamentalsSiteMapping, foundSiteMap);
    }

    [Fact]
    public void FindCSyntaxFundamentalsSanitizedWithAnchorReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = GetSiteMap().Find("c-syntax-fundamentals#hello-world");
        Assert.NotNull(foundSiteMap);
        Assert.Equal(CSyntaxFundamentalsSiteMapping, foundSiteMap);
    }
}
