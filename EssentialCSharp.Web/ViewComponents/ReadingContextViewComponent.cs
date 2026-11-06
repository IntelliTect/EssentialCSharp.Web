using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace EssentialCSharp.Web.ViewComponents;

/// <summary>
/// Emits <c>window.READING_CONTEXT</c> for the reading-time tracker. Renders nothing on pages that
/// are not book content (no <c>ViewBag.CurrentPageKey</c>), such as Identity pages and errors.
/// </summary>
public class ReadingContextViewComponent(
    IWordCountService wordCountService,
    SignInManager<EssentialCSharpWebUser> signInManager) : ViewComponent
{
    public IViewComponentResult Invoke()
    {
        if (ViewData["CurrentPageKey"] is not string { Length: > 0 } pageKey)
        {
            return Content(string.Empty);
        }

        PageWordStats? stats = wordCountService.GetPageStats(pageKey);
        return View(new ReadingContextViewModel(
            IsAuthenticated: signInManager.IsSignedIn(UserClaimsPrincipal),
            PageKey: pageKey,
            PageWords: stats?.PageWords ?? 0,
            ChapterWords: stats?.ChapterWords ?? 0,
            WordsBeforePage: stats?.WordsBeforePage ?? 0,
            ChapterStartWords: stats?.ChapterStartWords ?? 0,
            BookWords: stats?.BookWords ?? wordCountService.GetBookWordCount()));
    }
}

/// <summary>Serialized (camelCase) as <c>window.READING_CONTEXT</c>; property names are a contract with the client.</summary>
public record ReadingContextViewModel(
    bool IsAuthenticated,
    string PageKey,
    int PageWords,
    int ChapterWords,
    int WordsBeforePage,
    int ChapterStartWords,
    int BookWords);
