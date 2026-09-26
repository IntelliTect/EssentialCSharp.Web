using System.Globalization;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using Moq;

namespace EssentialCSharp.Web.Tests;

public class BookToolQueryServiceTests
{
    private static SiteMapping CreateSiteMapping(
        int chapterNumber,
        int pageNumber,
        int orderOnPage,
        string key,
        string rawHeading,
        int indentLevel,
        string chapterTitle = "Test Chapter",
        string? anchorId = null)
    {
        return new SiteMapping(
            keys: [key],
            primaryKey: key,
            pagePath: ["Chapters", chapterNumber.ToString("00", CultureInfo.InvariantCulture), "Pages", $"{pageNumber:00}.html"],
            chapterNumber: chapterNumber,
            pageNumber: pageNumber,
            orderOnPage: orderOnPage,
            chapterTitle: chapterTitle,
            rawHeading: rawHeading,
            anchorId: anchorId ?? key,
            indentLevel: indentLevel,
            contentHash: "TestHash",
            includeInSitemapXml: indentLevel == 0);
    }

    private static (BookToolQueryService Service, Mock<ISiteMappingService> SiteMappingServiceMock, Mock<IGuidelinesService> GuidelinesServiceMock) CreateService(
        IList<SiteMapping>? siteMappings = null,
        IReadOnlyList<GuidelineListing>? guidelines = null,
        string? baseUrl = null)
    {
        Mock<ISiteMappingService> siteMappingServiceMock = new();
        siteMappingServiceMock.Setup(m => m.SiteMappings).Returns(siteMappings ?? []);

        Mock<IGuidelinesService> guidelinesServiceMock = new();
        guidelinesServiceMock.Setup(m => m.Guidelines).Returns(guidelines ?? []);

        SiteSettings siteSettings = new() { BaseUrl = baseUrl ?? "https://essentialcsharp.com" };
        IOptions<SiteSettings> options = Options.Create(siteSettings);

        BookToolQueryService service = new(siteMappingServiceMock.Object, guidelinesServiceMock.Object, options);
        return (service, siteMappingServiceMock, guidelinesServiceMock);
    }

    [Test]
    public async Task GetChapterList_ReturnsTitleAndMappedTocData()
    {
        Mock<ISiteMappingService> siteMappingServiceMock = new();
        siteMappingServiceMock.Setup(m => m.SiteMappings).Returns((IList<SiteMapping>)[]);
        siteMappingServiceMock.Setup(m => m.GetTocData()).Returns(
        [
            new SiteMappingDto
            {
                Level = 0,
                Key = "chapter-1",
                Href = "chapter-1#intro",
                Title = "Chapter 1: Introducing C#",
                Items = []
            }
        ]);

        Mock<IGuidelinesService> guidelinesServiceMock = new();
        guidelinesServiceMock.Setup(m => m.Guidelines).Returns((IReadOnlyList<GuidelineListing>)[]);

        SiteSettings siteSettings = new() { BaseUrl = "https://essentialcsharp.com" };
        BookToolQueryService service = new(siteMappingServiceMock.Object, guidelinesServiceMock.Object, Options.Create(siteSettings));

        ChapterListToolResult result = service.GetChapterList();

        await Assert.That(result.Title).IsEqualTo("Essential C# - Table of Contents");
        await Assert.That(result.Chapters).Count().IsEqualTo(1);
        await Assert.That(result.Chapters[0].Key).IsEqualTo("chapter-1");
        await Assert.That(result.Chapters[0].Url).IsEqualTo("https://essentialcsharp.com/chapter-1#intro");
    }

    [Test]
    public async Task GetChapterSections_UnknownChapter_ThrowsMcpException()
    {
        (BookToolQueryService service, _, _) = CreateService();

        await Assert.That(() => service.GetChapterSections(99)).Throws<McpException>();
    }

    [Test]
    public async Task GetChapterSections_KnownChapter_ReturnsOrderedSections()
    {
        List<SiteMapping> mappings =
        [
            CreateSiteMapping(1, 2, 0, "page-2", "Second", 0),
            CreateSiteMapping(1, 1, 0, "page-1", "First", 0),
        ];
        (BookToolQueryService service, _, _) = CreateService(mappings);

        ChapterSectionsToolResult result = service.GetChapterSections(1);

        await Assert.That(result.ChapterNumber).IsEqualTo(1);
        await Assert.That(result.Sections).Count().IsEqualTo(2);
        await Assert.That(result.Sections[0].Key).IsEqualTo("page-1");
        await Assert.That(result.Sections[1].Key).IsEqualTo("page-2");
    }

    [Test]
    public async Task GetDirectContentUrl_BlankSectionKey_ThrowsMcpException()
    {
        (BookToolQueryService service, _, _) = CreateService();

        await Assert.That(() => service.GetDirectContentUrl(" ")).Throws<McpException>();
    }

    [Test]
    public async Task GetDirectContentUrl_UnknownSectionKey_ThrowsMcpException()
    {
        (BookToolQueryService service, _, _) = CreateService();

        await Assert.That(() => service.GetDirectContentUrl("does-not-exist")).Throws<McpException>();
    }

    [Test]
    public async Task GetDirectContentUrl_KnownSectionKey_ReturnsReference()
    {
        List<SiteMapping> mappings = [CreateSiteMapping(1, 1, 0, "page-1", "First", 0)];
        (BookToolQueryService service, _, _) = CreateService(mappings, baseUrl: "https://example.test/");

        BookSectionReferenceResult result = service.GetDirectContentUrl("page-1");

        await Assert.That(result.Key).IsEqualTo("page-1");
        await Assert.That(result.Url).IsEqualTo("https://example.test/page-1#page-1");
    }

    [Test]
    public async Task GetNavigationContext_BlankSectionKey_ThrowsMcpException()
    {
        (BookToolQueryService service, _, _) = CreateService();

        await Assert.That(() => service.GetNavigationContext(string.Empty)).Throws<McpException>();
    }

    [Test]
    public async Task GetNavigationContext_ReturnsPreviousNextAndParent()
    {
        List<SiteMapping> mappings =
        [
            CreateSiteMapping(1, 1, 0, "chapter-1", "Chapter 1", 0),
            CreateSiteMapping(1, 2, 0, "section-a", "Section A", 1),
            CreateSiteMapping(1, 3, 0, "section-b", "Section B", 1),
            CreateSiteMapping(1, 4, 0, "section-c", "Section C", 1),
        ];
        (BookToolQueryService service, _, _) = CreateService(mappings);

        NavigationContextToolResult result = service.GetNavigationContext("section-b");

        await Assert.That(result.Section.Key).IsEqualTo("section-b");
        await Assert.That(result.Previous!.Key).IsEqualTo("section-a");
        await Assert.That(result.Next!.Key).IsEqualTo("section-c");
        await Assert.That(result.Parent!.Key).IsEqualTo("chapter-1");
    }

    [Test]
    public async Task GetChapterSummary_UnknownChapter_ThrowsMcpException()
    {
        (BookToolQueryService service, _, _) = CreateService();

        await Assert.That(() => service.GetChapterSummary(42)).Throws<McpException>();
    }

    [Test]
    public async Task GetChapterSummary_FiltersGuidelinesByChapter()
    {
        List<SiteMapping> mappings = [CreateSiteMapping(1, 1, 0, "page-1", "First", 0)];
        List<GuidelineListing> guidelines =
        [
            new(GuidelineType.Do, "Do use meaningful names.", 1, "Chapter 1", "Naming", "Naming"),
            new(GuidelineType.Avoid, "Avoid unrelated chapter guideline.", 2, "Chapter 2", "Other", "Other"),
        ];
        (BookToolQueryService service, _, _) = CreateService(mappings, guidelines);

        ChapterSummaryToolResult result = service.GetChapterSummary(1);

        await Assert.That(result.ChapterNumber).IsEqualTo(1);
        await Assert.That(result.Guidelines).Count().IsEqualTo(1);
        await Assert.That(result.Guidelines[0].Guideline).IsEqualTo("Do use meaningful names.");
    }
}
