using EssentialCSharp.Web.Services;
using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Tests;

public class SiteMappingTests
{
    static SiteMapping HelloWorldSiteMapping { get; } = new(Key: "hello-world",
            PagePath: new string[]
            {
                "Chapters",
                "01",
                "Pages",
                "01.html"
            },
            ChapterNumber: 1,
            PageNumber: 1,
            ChapterTitle: "Introducing C#",
            RawHeading: "Introduction",
            AnchorId: "hello-world",
            IndentLevel: 0);
    static SiteMapping CSyntaxFundamentalsSiteMapping { get; } = new(Key: "c-syntax-fundamentals",
            PagePath: new string[]
            {
                "Chapters",
                "01",
                "Pages",
                "02.html"
            },
            ChapterNumber: 1,
            PageNumber: 2,
            ChapterTitle: "Introducing C#",
            RawHeading: "C# Syntax Fundamentals",
            AnchorId: "c-syntax-fundamentals",
            IndentLevel: 2);
    public static List<SiteMapping> GetSiteMap()
    {
        return new List<SiteMapping>()
        {
            HelloWorldSiteMapping,
            CSyntaxFundamentalsSiteMapping
    };
    }

    [Fact]
    public void FindHelloWorldWithAnchorSlugReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = SiteMapping.Find("hello-world#hello-world", GetSiteMap());
        Assert.NotNull(foundSiteMap);
        Assert.Equal(HelloWorldSiteMapping, foundSiteMap!);
    }
    [Fact]
    public void FindCSyntaxFundamentalsWithSpacesReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = SiteMapping.Find("C# Syntax Fundamentals", GetSiteMap());
        Assert.NotNull(foundSiteMap);
        Assert.Equal(CSyntaxFundamentalsSiteMapping, foundSiteMap!);
    }
    [Fact]
    public void FindCSyntaxFundamentalsWithSpacesAndAnchorReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = SiteMapping.Find("C# Syntax Fundamentals#hello-world", GetSiteMap());
        Assert.NotNull(foundSiteMap);
        Assert.Equal(CSyntaxFundamentalsSiteMapping, foundSiteMap!);
    }
    [Fact]
    public void FindCSyntaxFundamentalsSanitizedWithAnchorReturnsCorrectSiteMap()
    {
        SiteMapping? foundSiteMap = SiteMapping.Find("c-syntax-fundamentals#hello-world", GetSiteMap());
        Assert.NotNull(foundSiteMap);
        Assert.Equal(CSyntaxFundamentalsSiteMapping, foundSiteMap!);
    }
}
