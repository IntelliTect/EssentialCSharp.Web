using EssentialCSharp.Web.Extensions;

namespace EssentialCSharp.Web.Tests;

public class SiteMappingTests
{
    static SiteMapping HelloWorldSiteMapping { get; } = new(Key: "hello-world",
            PagePath:
            [
                "Chapters",
                "01",
                "Pages",
                "01.html"
            ],
            ChapterNumber: 1,
            PageNumber: 1,
            ChapterTitle: "Introducing C#",
            RawHeading: "Introduction",
            AnchorId: "hello-world",
            IndentLevel: 0
    );

    static SiteMapping CSyntaxFundamentalsSiteMapping { get; } = new(Key: "c-syntax-fundamentals",
            PagePath:
            [
                "Chapters",
                "01",
                "Pages",
                "02.html"
            ],
            ChapterNumber: 1,
            PageNumber: 2,
            ChapterTitle: "Introducing C#",
            RawHeading: "C# Syntax Fundamentals",
            AnchorId: "c-syntax-fundamentals",
            IndentLevel: 2
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
}
