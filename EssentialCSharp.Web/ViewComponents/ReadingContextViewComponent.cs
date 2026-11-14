using System.Security.Claims;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.ViewComponents;

/// <summary>
/// Emits <c>window.READING_CONTEXT</c> for the reading-time tracker. Renders nothing on pages that
/// are not book content (no <c>ViewBag.CurrentPageKey</c>), such as Identity pages and errors.
/// </summary>
public sealed partial class ReadingContextViewComponent(
    IWordCountService wordCountService,
    IReadingProfileService readingProfileService,
    SignInManager<EssentialCSharpWebUser> signInManager,
    IOptions<ReadingOptions> options,
    ILogger<ReadingContextViewComponent> logger) : ViewComponent
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not load the reading profile for the reading context; continuing without a personal WPM")]
    private static partial void LogProfileUnavailable(ILogger logger, Exception exception);

    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (ViewData["CurrentPageKey"] is not string { Length: > 0 } pageKey)
        {
            return Content(string.Empty);
        }

        bool isAuthenticated = signInManager.IsSignedIn(UserClaimsPrincipal);
        string? userId = isAuthenticated ? UserClaimsPrincipal.FindFirstValue(ClaimTypes.NameIdentifier) : null;
        int? wpm = userId is null ? null : await GetWpmAsync(userId);
        ReadingOptions reading = options.Value;

        PageWordStats? stats = wordCountService.GetPageStats(pageKey);
        return View(new ReadingContextViewModel(
            IsAuthenticated: isAuthenticated,
            PageKey: pageKey,
            PageWords: stats?.PageWords ?? 0,
            ChapterWords: stats?.ChapterWords ?? 0,
            WordsBeforePage: stats?.WordsBeforePage ?? 0,
            ChapterStartWords: stats?.ChapterStartWords ?? 0,
            BookWords: stats?.BookWords ?? wordCountService.GetBookWordCount(),
            Wpm: wpm,
            DefaultWpm: reading.DefaultWpm,
            MaxCreditWpm: reading.MaxCreditWpm,
            IdleThresholdSeconds: reading.IdleThresholdSeconds,
            PeriodicFlushActiveSeconds: reading.PeriodicFlushActiveSeconds));
    }

    /// <summary>The signed-in reader's derived WPM rounded to whole words per minute, or null when unknown.</summary>
    private async Task<int?> GetWpmAsync(string userId)
    {
        try
        {
            ReadingProfileSummary profile = await readingProfileService.GetProfileAsync(userId, HttpContext.RequestAborted);
            return profile.Wpm is { } value ? (int)Math.Round(value) : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A reading-profile outage must not break rendering of book pages.
            LogProfileUnavailable(logger, ex);
            return null;
        }
    }
}

/// <summary>
/// Serialized (camelCase) as <c>window.READING_CONTEXT</c>; property names are a contract with the client.
/// <paramref name="Wpm"/> is null for anonymous readers or readers with no recorded reading time.
/// </summary>
public record ReadingContextViewModel(
    bool IsAuthenticated,
    string PageKey,
    int PageWords,
    int ChapterWords,
    int WordsBeforePage,
    int ChapterStartWords,
    int BookWords,
    int? Wpm,
    int DefaultWpm,
    int MaxCreditWpm,
    int IdleThresholdSeconds,
    int PeriodicFlushActiveSeconds);
