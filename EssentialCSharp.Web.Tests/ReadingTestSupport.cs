using System.Net;
using System.Security.Claims;
using System.Text.Json;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using HtmlAgilityPack;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TUnit.AspNetCore;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Known content statistics and reading options shared by the reading-tracker tests, so that every
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
    public static readonly string MaxLengthPageKey = new('k', Controllers.ReadingIntervalRequest.PageKeyMaxLength);

    /// <summary>The page rendered at <c>/hello-world</c>; deliberately distinct values so swapped fields are detected.</summary>
    public const string ContentPageKey = "hello-world";
    public static readonly PageWordStats ContentPageStats = new(1234, 3000, 400, 100, 5150);

    public const int BookWords = 5150;

    /// <summary>
    /// Explicit options, a fresh instance per call so no test can leak mutations into another. Nothing here relies on
    /// <see cref="ReadingOptions"/> defaults, and every value differs from its default.
    /// </summary>
    public static ReadingOptions CreateOptions() => new()
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

    public static void UseFakes(IServiceCollection services, IWordCountService wordCounts, ReadingOptions? options = null)
    {
        services.RemoveAll<IWordCountService>();
        services.AddSingleton(wordCounts);
        services.RemoveAll<IOptions<ReadingOptions>>();
        services.AddSingleton<IOptions<ReadingOptions>>(Microsoft.Extensions.Options.Options.Create(options ?? CreateOptions()));
    }

    /// <summary>A word-count service backed by a fixed dictionary; unknown pages have no stats and zero words.</summary>
    public sealed class FakeWordCountService(IReadOnlyDictionary<string, PageWordStats> pages) : IWordCountService
    {
        /// <summary>The standard fixture pages (<see cref="SmallPage"/>, <see cref="LargePage"/>, ...).</summary>
        public FakeWordCountService(bool includeContentPage = true)
            : this(CreateDefaultPages(includeContentPage))
        {
        }

        /// <summary>A service in which only <paramref name="pageKey"/> is readable, with <paramref name="words"/> words.</summary>
        public static FakeWordCountService WithSinglePage(string pageKey, int words) =>
            new(new Dictionary<string, PageWordStats> { [pageKey] = new(words, words, 0, 0, words) });

        private static Dictionary<string, PageWordStats> CreateDefaultPages(bool includeContentPage)
        {
            Dictionary<string, PageWordStats> result = new()
            {
                [SmallPage] = new(SmallPageWords, SmallPageWords, 0, 0, BookWords),
                [LargePage] = new(LargePageWords, LargePageWords, 0, 0, BookWords),
                [EmptyPage] = new(0, 0, 0, 0, BookWords),
                [MaxLengthPageKey] = new(SmallPageWords, SmallPageWords, 0, 0, BookWords),
            };
            if (includeContentPage)
            {
                result[ContentPageKey] = ContentPageStats;
            }

            return result;
        }

        public bool IsReady => true;
        public int GetPageWordCount(string pageKey) => pages.TryGetValue(pageKey, out PageWordStats? s) ? s.PageWords : 0;
        public int GetBookWordCount() => pages.Values.Select(p => p.BookWords).DefaultIfEmpty(0).Max();
        public PageWordStats? GetPageStats(string pageKey) => pages.GetValueOrDefault(pageKey);
    }

    public static async Task SeedProfileAsync(
        IServiceProvider rootServices, string userId, long words, long seconds, DateTime? updatedAtUtc = null)
    {
        using IServiceScope scope = rootServices.CreateScope();
        EssentialCSharpWebContext context = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
        context.UserReadingProfiles.Add(new UserReadingProfile
        {
            UserId = userId,
            TotalWordsRead = words,
            TotalActiveSeconds = seconds,
            UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow
        });
        await context.SaveChangesAsync();
    }

    // ---- credentials ----

    /// <summary>The cookie header (sign-in plus antiforgery cookie) with the matching request token and header name.</summary>
    public sealed record AntiforgeryCredentials(string CookieHeader, string HeaderName, string RequestToken);

    /// <summary>A <c>Cookie</c> header value that signs the request in as <paramref name="userId"/>; no antiforgery material.</summary>
    public static async Task<string> CreateSignedInCookieHeaderAsync(
        TracedWebApplicationFactory<Program> factory, string userId, Func<ClaimsPrincipal, ClaimsPrincipal>? transformPrincipal = null)
    {
        (string cookieName, string cookieValue) =
            await McpTestHelper.CreateIdentityApplicationCookieAsync(factory.Services, userId, transformPrincipal);
        return $"{cookieName}={cookieValue}";
    }

    /// <summary>
    /// Mints the sign-in cookie, the antiforgery cookie and the request token for <paramref name="userId"/> straight
    /// from <see cref="IAntiforgery"/>, using the names from the configured <see cref="AntiforgeryOptions"/>.
    /// </summary>
    public static async Task<AntiforgeryCredentials> CreateSignedInAntiforgeryCredentialsAsync(
        TracedWebApplicationFactory<Program> factory, string userId, Func<ClaimsPrincipal, ClaimsPrincipal>? transformPrincipal = null)
    {
        string signInCookie = await CreateSignedInCookieHeaderAsync(factory, userId, transformPrincipal);

        using IServiceScope scope = factory.Services.CreateScope();
        IServiceProvider services = scope.ServiceProvider;
        EssentialCSharpWebUser user = await services.GetRequiredService<UserManager<EssentialCSharpWebUser>>()
            .FindByIdAsync(userId)
            ?? throw new InvalidOperationException($"User {userId} does not exist.");
        var context = new DefaultHttpContext
        {
            RequestServices = services,
            User = ApplyTransform(await services.GetRequiredService<IUserClaimsPrincipalFactory<EssentialCSharpWebUser>>().CreateAsync(user), transformPrincipal)
        };

        AntiforgeryTokenSet tokens = services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
        string antiforgeryCookieName = AntiforgeryCookieName(services);
        string antiforgeryCookie = context.Response.Headers.SetCookie
            .Select(value => value?.Split(';', 2)[0])
            .OfType<string>()
            .Single(value => value.StartsWith(antiforgeryCookieName + "=", StringComparison.Ordinal));
        return new AntiforgeryCredentials(
            $"{signInCookie}; {antiforgeryCookie}",
            tokens.HeaderName ?? throw new InvalidOperationException("The antiforgery header name is not configured."),
            tokens.RequestToken ?? throw new InvalidOperationException("No antiforgery request token."));
    }

    /// <summary>Creates a client signed in as <paramref name="userId"/>, optionally carrying the antiforgery cookie and header token.</summary>
    public static async Task<HttpClient> CreateSignedInClientAsync(
        TracedWebApplicationFactory<Program> factory, string userId, bool includeAntiforgery = true)
    {
        HttpClient client = McpTestHelper.CreateClient(factory);
        if (includeAntiforgery)
        {
            AntiforgeryCredentials credentials = await CreateSignedInAntiforgeryCredentialsAsync(factory, userId);
            client.DefaultRequestHeaders.Add(credentials.HeaderName, credentials.RequestToken);
            client.DefaultRequestHeaders.Add("Cookie", credentials.CookieHeader);
        }
        else
        {
            client.DefaultRequestHeaders.Add("Cookie", await CreateSignedInCookieHeaderAsync(factory, userId));
        }

        return client;
    }

    private static ClaimsPrincipal ApplyTransform(ClaimsPrincipal principal, Func<ClaimsPrincipal, ClaimsPrincipal>? transform) =>
        transform is null ? principal : transform(principal);

    public static string AntiforgeryCookieName(IServiceProvider services) =>
        services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.Name
            ?? throw new InvalidOperationException("The antiforgery cookie name is not configured.");

    // ---- problem responses ----

    /// <summary>A response read as a problem document; <see cref="Root"/> is null when the body is not JSON.</summary>
    public sealed record ProblemResponse(HttpStatusCode Status, string? MediaType, string Raw, JsonElement? Root)
    {
        public bool HasErrorsMember => Root is { ValueKind: JsonValueKind.Object } root && root.TryGetProperty("errors", out _);

        public IReadOnlyList<string> ErrorKeys =>
            Root is { ValueKind: JsonValueKind.Object } root
                && root.TryGetProperty("errors", out JsonElement errors)
                && errors.ValueKind == JsonValueKind.Object
                    ? errors.EnumerateObject().Select(p => p.Name).ToArray()
                    : [];

        public override string ToString() =>
            $"{(int)Status} {MediaType ?? "<no content type>"}: {(Raw.Length == 0 ? "<empty body>" : Raw)}";
    }

    public static async Task<ProblemResponse> ReadProblemAsync(HttpResponseMessage response)
    {
        string raw = await response.Content.ReadAsStringAsync();
        JsonElement? root = null;
        try
        {
            root = JsonDocument.Parse(raw).RootElement.Clone();
        }
        catch (JsonException)
        {
            // Left null: callers assert on Root and report the raw body.
        }

        return new ProblemResponse(response.StatusCode, response.Content.Headers.ContentType?.MediaType, raw, root);
    }

    /// <summary>
    /// Asserts a 400 <c>application/problem+json</c> validation problem whose <c>errors</c> member names
    /// <paramref name="keyParts"/> (every part must appear, case-insensitively, in a single error key, so a test can
    /// require both the interval index and the field, e.g. <c>"Intervals[1]"</c> and <c>"ActiveSeconds"</c>, without
    /// pinning the exact path format). Message text is not asserted.
    /// </summary>
    public static async Task AssertValidationProblemAsync(HttpResponseMessage response, params string[] keyParts)
    {
        ProblemResponse problem = await ReadProblemAsync(response);
        using (Assert.Multiple())
        {
            await Assert.That(problem.Status).IsEqualTo(HttpStatusCode.BadRequest).Because(problem.ToString());
            await Assert.That(problem.MediaType).IsEqualTo("application/problem+json").Because(problem.ToString());
            await Assert.That(problem.Root).IsNotNull().Because($"the body is not JSON: {problem}");
            await Assert.That(problem.ErrorKeys)
                .Contains(key => keyParts.All(part => key.Contains(part, StringComparison.OrdinalIgnoreCase)))
                .Because($"expected an error key containing {string.Join(" and ", keyParts.Select(p => $"'{p}'"))} in {problem}");
        }
    }

    // ---- rendered page helpers ----

    /// <summary>Asserts a 200 response and returns its HTML.</summary>
    public static async Task<string> ReadOkHtmlAsync(HttpResponseMessage response)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    private static HtmlDocument LoadHtml(string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        return document;
    }

    /// <summary>
    /// Parses the object assigned to <c>window.READING_CONTEXT</c> by an inline script, or returns null when no
    /// script on the page assigns it.
    /// </summary>
    public static JsonDocument? TryParseReadingContext(string html)
    {
        const string Assignment = "window.READING_CONTEXT";
        foreach (HtmlNode script in LoadHtml(html).DocumentNode.SelectNodes("//script") ?? Enumerable.Empty<HtmlNode>())
        {
            string text = script.InnerText;
            int assignment = text.IndexOf(Assignment, StringComparison.Ordinal);
            if (assignment < 0)
            {
                continue;
            }

            string afterName = text[(assignment + Assignment.Length)..].TrimStart();
            if (afterName.StartsWith('='))
            {
                return JsonDocument.Parse(afterName[1..].Trim().TrimEnd(';'));
            }
        }

        return null;
    }

    public static JsonDocument ParseReadingContext(string html) =>
        TryParseReadingContext(html)
        ?? throw new InvalidOperationException("The page has no script assigning window.READING_CONTEXT.");

    /// <summary>Returns the named field, failing with its name rather than a bare KeyNotFoundException.</summary>
    public static JsonElement Field(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new InvalidOperationException($"READING_CONTEXT has no '{name}' field.");

    public static string? CsrfToken(string html) =>
        LoadHtml(html).DocumentNode.SelectSingleNode("//meta[@name='csrf-token']")?.GetAttributeValue("content", null!);

    public static string? PageTitle(string html) =>
        LoadHtml(html).DocumentNode.SelectSingleNode("//title")?.InnerText.Trim();
}
