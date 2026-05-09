using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Services;

public interface IBookToolQueryService
{
    ChapterListToolResult GetChapterList();
    ChapterSectionsToolResult GetChapterSections(int chapter);
    BookSectionReferenceResult GetDirectContentUrl(string sectionKey);
    NavigationContextToolResult GetNavigationContext(string sectionKey);
    ChapterSummaryToolResult GetChapterSummary(int chapter);
}
