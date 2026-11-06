using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TUnit.AspNetCore;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Known content statistics and reading options shared by the reading-tracker integration tests, so that every
/// asserted number comes from a literal here rather than from the content package or the app's defaults.
/// </summary>
internal static class ReadingTestSupport
{
    public const string SmallPage = "reading-test-small";
    public const int SmallPageWords = 150;
    public const string LargePage = "reading-test-large";
    public const int LargePageWords = 5000;
    public const string EmptyPage = "reading-test-empty";

    /// <summary>A page whose key is exactly <see cref="Controllers.ReadingIntervalRequest.PageKeyMaxLength"/> characters.</summary>
    public static readonly string MaxLengthPageKey = new('k', 256);

    /// <summary>The page rendered at <c>/hello-world</c>; deliberately distinct values so swapped fields are detected.</summary>
    public const string ContentPageKey = "hello-world";
    public static readonly PageWordStats ContentPageStats = new(1234, 3000, 400, 100, 5150);

    public static readonly ChapterWordCount[] Chapters = [new(1, 3000), new(2, 2150)];
    public const int BookWords = 5150;

    /// <summary>Explicit options: nothing here relies on <see cref="ReadingOptions"/> defaults.</summary>
    public static ReadingOptions Options { get; } = new()
    {
        HardCutoffWpm = 900,
        SlowOutlierFactor = 0.25,
        MaxIntervalsPerRequest = 50,
        DefaultWpm = 210,
        MaxCreditWpm = 810,
        IdleThresholdSeconds = 310,
        PeriodicFlushActiveSeconds = 65,
        MaxActiveSecondsPerInterval = 1800
    };

    public static void UseFakes(IServiceCollection services, IWordCountService wordCounts)
    {
        services.RemoveAll<IWordCountService>();
        services.AddSingleton(wordCounts);
        services.RemoveAll<IOptions<ReadingOptions>>();
        services.AddSingleton<IOptions<ReadingOptions>>(Microsoft.Extensions.Options.Options.Create(Options));
    }

    public sealed class FakeWordCountService : IWordCountService
    {
        private readonly Dictionary<string, PageWordStats> _pages = new()
        {
            [SmallPage] = new(SmallPageWords, SmallPageWords, 0, 0, BookWords),
            [LargePage] = new(LargePageWords, LargePageWords, 0, 0, BookWords),
            [EmptyPage] = new(0, 0, 0, 0, BookWords),
            [MaxLengthPageKey] = new(SmallPageWords, SmallPageWords, 0, 0, BookWords),
            [ContentPageKey] = ContentPageStats
        };

        public FakeWordCountService(bool includeContentPage = true)
        {
            if (!includeContentPage)
            {
                _pages.Remove(ContentPageKey);
            }
        }

        public bool IsReady => true;
        public int GetPageWordCount(string pageKey) => _pages.TryGetValue(pageKey, out PageWordStats? s) ? s.PageWords : 0;
        public int GetBookWordCount() => BookWords;
        public PageWordStats? GetPageStats(string pageKey) => _pages.GetValueOrDefault(pageKey);
        public IReadOnlyList<ChapterWordCount> GetChapterWordCounts() => Chapters;
    }

    public static async Task SeedProfileAsync(IServiceProvider rootServices, string userId, long words, long seconds)
    {
        using IServiceScope scope = rootServices.CreateScope();
        EssentialCSharpWebContext context = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
        context.UserReadingProfiles.Add(new UserReadingProfile
        {
            UserId = userId,
            TotalWordsRead = words,
            TotalActiveSeconds = seconds,
            UpdatedAtUtc = DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Mints the antiforgery cookie and request token for <paramref name="userId"/> straight from <see cref="IAntiforgery"/>,
    /// using the cookie name from the configured <see cref="AntiforgeryOptions"/>.
    /// </summary>
    public static async Task<(string CookieHeader, string HeaderName, string RequestToken)> CreateAuthenticatedCredentialsAsync(
        TracedWebApplicationFactory<Program> factory, string userId, bool includeAntiforgery = true)
    {
        (string cookieName, string cookieValue) =
            await McpTestHelper.CreateIdentityApplicationCookieAsync(factory.Services, userId);
        string cookieHeader = $"{cookieName}={cookieValue}";
        if (!includeAntiforgery)
        {
            return (cookieHeader, "", "");
        }

        using IServiceScope scope = factory.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        EssentialCSharpWebUser user = await services.GetRequiredService<UserManager<EssentialCSharpWebUser>>()
            .FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User {userId} does not exist.");
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = await services.GetRequiredService<IUserClaimsPrincipalFactory<EssentialCSharpWebUser>>().CreateAsync(user)
        };

        AntiforgeryTokenSet tokens = services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        string antiforgeryCookieName = AntiforgeryCookieName(services);
        string antiforgeryCookie = context.Response.Headers.SetCookie
            .Select(value => value?.Split(';', 2)[0])
            .OfType<string>()
            .Single(value => value.StartsWith(antiforgeryCookieName + "=", StringComparison.Ordinal));
        return (
            $"{cookieHeader}; {antiforgeryCookie}",
            tokens.HeaderName ?? "RequestVerificationToken",
            tokens.RequestToken ?? throw new InvalidOperationException("No antiforgery request token."));
    }

    public static string AntiforgeryCookieName(IServiceProvider services) =>
        services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.Name
            ?? throw new InvalidOperationException("The antiforgery cookie name is not configured.");
}
