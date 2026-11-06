using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Controllers;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

public class ReadingControllerTests : IntegrationTestBase
{
    // Baseline profile: 1200 words in 360 seconds = 200 WPM.
    private const long BaselineWords = 1200;
    private const long BaselineSeconds = 360;
    private const double BaselineWpm = 200;

    // The content package is a placeholder in many environments, so use fixed, known page word counts.
    private static readonly Dictionary<string, int> PageWordCounts = new()
    {
        ["reading-test-small"] = 150,
        ["reading-test-large"] = 5000
    };

    private static readonly ChapterWordCount[] ChapterWordCounts =
    [
        new(1, 3000),
        new(2, 2150)
    ];

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        services.RemoveAll<IWordCountService>();
        services.AddSingleton<IWordCountService>(new FakeWordCountService());
    }

    private sealed class FakeWordCountService : IWordCountService
    {
        public int GetPageWordCount(string pageKey) => PageWordCounts.GetValueOrDefault(pageKey);
        public int GetChapterWordCount(int chapterNumber) =>
            ChapterWordCounts.Where(c => c.ChapterNumber == chapterNumber).Sum(c => c.WordCount);
        public int GetBookWordCount() => ChapterWordCounts.Sum(c => c.WordCount);
        public int GetWordsBeforePage(string pageKey) => 0;
        public int GetChapterStartWords(string pageKey) => 0;
        public IReadOnlyList<ChapterWordCount> GetChapterWordCounts() => ChapterWordCounts;
    }

    private ReadingOptions Options =>
        Factory.Services.GetRequiredService<IOptions<ReadingOptions>>().Value;

    // ---- authentication ----

    [Test]
    [Arguments("GET", "/api/reading/profile")]
    [Arguments("POST", "/api/reading/session")]
    [Arguments("POST", "/api/reading/reset")]
    public async Task ProtectedEndpoints_Anonymous_Return401WithoutRedirect(string method, string url)
    {
        PageInfo page = GetLargestPage();
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = JsonBody($$"""{"intervals":[{"pageKey":"{{page.Key}}","wordsRead":10,"activeSeconds":10}]}""");
        }

        using HttpResponseMessage response = await client.SendAsync(request);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(response.Headers.Location).IsNull();
        }
    }

    [Test]
    [Arguments("/api/reading/session")]
    [Arguments("/api/reading/reset")]
    public async Task WriteEndpoints_WithoutAntiforgeryToken_AreRejectedAndDoNotChangeProfile(string url)
    {
        PageInfo page = GetLargestPage();
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId, includeAntiforgery: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonBody($$"""{"intervals":[{"pageKey":"{{page.Key}}","wordsRead":10,"activeSeconds":10}]}""")
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    // ---- GET profile ----

    [Test]
    public async Task GetProfile_UserWithoutData_ReturnsZeroTotalsAndNullWpm()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-empty");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await client.GetAsync("/api/reading/profile");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        using (Assert.Multiple())
        {
            await Assert.That(body.GetProperty("totalWordsRead").GetInt64()).IsEqualTo(0L);
            await Assert.That(body.GetProperty("totalActiveSeconds").GetInt64()).IsEqualTo(0L);
            await Assert.That(body.GetProperty("wpm").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task GetProfile_UserWithData_ReturnsTotalsAndDerivedWpm()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await client.GetAsync("/api/reading/profile");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        using (Assert.Multiple())
        {
            await Assert.That(body.GetProperty("totalWordsRead").GetInt64()).IsEqualTo(BaselineWords);
            await Assert.That(body.GetProperty("totalActiveSeconds").GetInt64()).IsEqualTo(BaselineSeconds);
            await Assert.That(body.GetProperty("wpm").GetDouble()).IsEqualTo(BaselineWpm).Within(0.001);
        }
    }

    // ---- POST session: scoring behavior ----

    [Test]
    public async Task PostSession_NormalInterval_AddsWordsAndTimeAndReturnsUpdatedProfile()
    {
        PageInfo page = GetLargestPage();
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        int words = Math.Min(page.WordCount, 100);
        int seconds = 30;

        using HttpResponseMessage response = await PostSessionAsync(client, Interval(page.Key, words, seconds));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        long expectedWords = BaselineWords + words;
        long expectedSeconds = BaselineSeconds + seconds;
        using (Assert.Multiple())
        {
            await Assert.That(body.GetProperty("totalWordsRead").GetInt64()).IsEqualTo(expectedWords);
            await Assert.That(body.GetProperty("totalActiveSeconds").GetInt64()).IsEqualTo(expectedSeconds);
            await Assert.That(body.GetProperty("wpm").GetDouble())
                .IsEqualTo(expectedWords / (expectedSeconds / 60.0)).Within(0.001);
        }
        await AssertProfileAsync(userId, expectedWords, expectedSeconds);
    }

    [Test]
    public async Task PostSession_VerySlowInterval_CountsForLessThanItsWallClockTime()
    {
        PageInfo page = GetLargestPage();
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        int words = Math.Min(page.WordCount, 50);
        int wallClockSeconds = 3600;

        using HttpResponseMessage response = await PostSessionAsync(client, Interval(page.Key, words, wallClockSeconds));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        UserReadingProfile profile = await GetProfileAsync(userId);
        long creditedSeconds = profile.TotalActiveSeconds - BaselineSeconds;
        using (Assert.Multiple())
        {
            await Assert.That(profile.TotalWordsRead).IsEqualTo(BaselineWords + words);
            await Assert.That(creditedSeconds).IsGreaterThan(0L);
            await Assert.That(creditedSeconds).IsLessThan((long)wallClockSeconds);
            // Credited at roughly the slow-outlier floor speed.
            double creditedWpm = words / (creditedSeconds / 60.0);
            await Assert.That(creditedWpm).IsEqualTo(Options.SlowOutlierFactor * BaselineWpm).Within(1.0);
        }
    }

    [Test]
    public async Task PostSession_IntervalAtHardCutoff_IsAccepted()
    {
        PageInfo page = GetLargestPage();
        int seconds = 20;
        int words = (int)(Options.HardCutoffWpm * seconds / 60.0);
        await Assert.That(page.WordCount).IsGreaterThanOrEqualTo(words + 1);
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(client, Interval(page.Key, words, seconds));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(userId, BaselineWords + words, BaselineSeconds + seconds);
    }

    [Test]
    public async Task PostSession_ImplausiblyFastInterval_IsIgnoredAndStillSucceeds()
    {
        PageInfo page = GetLargestPage();
        int seconds = 20;
        int words = (int)(Options.HardCutoffWpm * seconds / 60.0) + 1;
        await Assert.That(page.WordCount).IsGreaterThanOrEqualTo(words);
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(client, Interval(page.Key, words, seconds));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_WordsAbovePageWordCount_AreCappedAtPageWordCount()
    {
        PageInfo page = GetSmallestPage();
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-cap");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        int claimedWords = page.WordCount + 1;
        // Long enough that the interval stays below the hard cutoff.
        int seconds = (int)Math.Ceiling(claimedWords * 60.0 / Options.HardCutoffWpm) + 5;

        using HttpResponseMessage response = await PostSessionAsync(client, Interval(page.Key, claimedWords, seconds));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        UserReadingProfile profile = await GetProfileAsync(userId);
        await Assert.That(profile.TotalWordsRead).IsEqualTo((long)page.WordCount);
    }

    [Test]
    public async Task PostSession_AtMaximumIntervalsPerRequest_IsAccepted()
    {
        PageInfo page = GetLargestPage();
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-batch-max");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        int count = Options.MaxIntervalsPerRequest;

        using HttpResponseMessage response = await PostSessionAsync(
            client,
            Enumerable.Repeat(Interval(page.Key, 10, 10), count).ToArray());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(userId, 10L * count, 10L * count);
    }

    [Test]
    public async Task PostSession_OneOverMaximumIntervalsPerRequest_IsRejectedAndDoesNotChangeProfile()
    {
        PageInfo page = GetLargestPage();
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        int count = Options.MaxIntervalsPerRequest + 1;

        using HttpResponseMessage response = await PostSessionAsync(
            client,
            Enumerable.Repeat(Interval(page.Key, 10, 10), count).ToArray());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_PageKeyLength_AtLimitIsUnknownKeyAndOneOverFailsLengthValidation()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage atLimit = await PostSessionAsync(
            client, Interval(new string('p', ReadingIntervalRequest.PageKeyMaxLength), 10, 10));
        using HttpResponseMessage overLimit = await PostSessionAsync(
            client, Interval(new string('p', ReadingIntervalRequest.PageKeyMaxLength + 1), 10, 10));

        string atLimitBody = await atLimit.Content.ReadAsStringAsync();
        string overLimitBody = await overLimit.Content.ReadAsStringAsync();
        using (Assert.Multiple())
        {
            await Assert.That(atLimit.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(atLimitBody).Contains("Unknown page key");
            await Assert.That(overLimit.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(overLimitBody).DoesNotContain("Unknown page key");
        }
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    // ---- POST session: invalid input ----

    [Test]
    [Arguments("empty body", "")]
    [Arguments("JSON null", "null")]
    [Arguments("malformed JSON", "{not json")]
    [Arguments("bare array (old wire format)", """[{"pageKey":"{KEY}","wordsRead":10,"activeSeconds":10}]""")]
    [Arguments("missing intervals", "{}")]
    [Arguments("null intervals", """{"intervals":null}""")]
    [Arguments("empty intervals", """{"intervals":[]}""")]
    [Arguments("null interval entry", """{"intervals":[null]}""")]
    [Arguments("zero words", """{"intervals":[{"pageKey":"{KEY}","wordsRead":0,"activeSeconds":10}]}""")]
    [Arguments("negative words", """{"intervals":[{"pageKey":"{KEY}","wordsRead":-1,"activeSeconds":10}]}""")]
    [Arguments("zero seconds", """{"intervals":[{"pageKey":"{KEY}","wordsRead":10,"activeSeconds":0}]}""")]
    [Arguments("negative seconds", """{"intervals":[{"pageKey":"{KEY}","wordsRead":10,"activeSeconds":-1}]}""")]
    [Arguments("int.MaxValue words", """{"intervals":[{"pageKey":"{KEY}","wordsRead":2147483647,"activeSeconds":10}]}""")]
    [Arguments("int.MaxValue seconds", """{"intervals":[{"pageKey":"{KEY}","wordsRead":10,"activeSeconds":2147483647}]}""")]
    [Arguments("missing page key", """{"intervals":[{"wordsRead":10,"activeSeconds":10}]}""")]
    [Arguments("empty page key", """{"intervals":[{"pageKey":"","wordsRead":10,"activeSeconds":10}]}""")]
    [Arguments("unknown page key", """{"intervals":[{"pageKey":"no-such-page","wordsRead":10,"activeSeconds":10}]}""")]
    [Arguments("one bad interval among good ones", """{"intervals":[{"pageKey":"{KEY}","wordsRead":10,"activeSeconds":10},{"pageKey":"{KEY}","wordsRead":0,"activeSeconds":10}]}""")]
    public async Task PostSession_InvalidInput_IsRejectedAndLeavesProfileUnchanged(string scenario, string bodyTemplate)
    {
        PageInfo page = GetLargestPage();
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reading/session")
        {
            Content = JsonBody(bodyTemplate.Replace("{KEY}", page.Key, StringComparison.Ordinal))
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(scenario);
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_ValidationFailure_ReturnsProblemDetails()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(client, Interval("no-such-page", 10, 10));

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            await Assert.That(body.GetProperty("status").GetInt32()).IsEqualTo(400);
            await Assert.That(body.TryGetProperty("errors", out _)).IsTrue();
        }
    }

    // ---- user isolation ----

    [Test]
    public async Task PostSession_UpdatesOnlyTheAuthenticatedUsersProfile()
    {
        PageInfo page = GetLargestPage();
        string callerId = await CreateUserWithBaselineProfileAsync();
        string otherId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(callerId);

        using HttpResponseMessage response = await PostSessionAsync(client, Interval(page.Key, 50, 30));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(callerId, BaselineWords + 50, BaselineSeconds + 30);
        await AssertProfileAsync(otherId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task GetProfile_ReturnsOnlyTheAuthenticatedUsersTotals()
    {
        string callerId = await McpTestHelper.CreateUserAsync(Factory, "reading-caller");
        await SeedProfileAsync(await McpTestHelper.CreateUserAsync(Factory, "reading-other"), 5000, 100);
        using HttpClient client = await CreateAuthenticatedClientAsync(callerId);

        using HttpResponseMessage response = await client.GetAsync("/api/reading/profile");

        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        await Assert.That(body.GetProperty("totalWordsRead").GetInt64()).IsEqualTo(0L);
    }

    // ---- reset ----

    [Test]
    public async Task Reset_RemovesOnlyTheCallersProfile()
    {
        string callerId = await CreateUserWithBaselineProfileAsync();
        string otherId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(callerId);

        using HttpResponseMessage response = await client.PostAsync("/api/reading/reset", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        bool callerHasProfile = await InServiceScopeAsync(services =>
            services.GetRequiredService<EssentialCSharpWebContext>()
                .UserReadingProfiles.AnyAsync(p => p.UserId == callerId));
        await Assert.That(callerHasProfile).IsFalse();
        await AssertProfileAsync(otherId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task Reset_UserWithoutData_Returns204()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-reset-empty");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await client.PostAsync("/api/reading/reset", null);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
    }

    // ---- concurrency ----

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PostSession_ConcurrentRequests_SumCorrectly(bool userAlreadyHasProfile)
    {
        PageInfo page = GetLargestPage();
        // Stay below the per-user write rate limit.
        const int parallelRequests = 16;
        const int wordsPerRequest = 40;
        const int secondsPerRequest = 20;
        string userId = userAlreadyHasProfile
            ? await CreateUserWithBaselineProfileAsync()
            : await McpTestHelper.CreateUserAsync(Factory, "reading-concurrent");
        long baselineWords = userAlreadyHasProfile ? BaselineWords : 0;
        long baselineSeconds = userAlreadyHasProfile ? BaselineSeconds : 0;

        // Authenticate every client up front so the requests themselves start together.
        var clients = new List<HttpClient>();
        try
        {
            for (int i = 0; i < parallelRequests; i++)
            {
                clients.Add(await CreateAuthenticatedClientAsync(userId));
            }

            using var startBarrier = new ManualResetEventSlim(false);
            Task<HttpStatusCode>[] requests = clients.Select(client => Task.Run(async () =>
            {
                startBarrier.Wait();
                using HttpResponseMessage response = await PostSessionAsync(
                    client, Interval(page.Key, wordsPerRequest, secondsPerRequest));
                return response.StatusCode;
            })).ToArray();

            startBarrier.Set();
            HttpStatusCode[] statuses = await Task.WhenAll(requests);

            await Assert.That(statuses.All(status => status == HttpStatusCode.OK)).IsTrue();
            await AssertProfileAsync(
                userId,
                baselineWords + (long)parallelRequests * wordsPerRequest,
                baselineSeconds + (long)parallelRequests * secondsPerRequest);
        }
        finally
        {
            foreach (HttpClient client in clients)
            {
                client.Dispose();
            }
        }
    }

    // ---- book stats ----

    [Test]
    public async Task GetBookStats_IsPublicAndTotalsMatchChapters()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();

        using HttpResponseMessage response = await client.GetAsync("/api/reading/book-stats");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        BookStatsResponse? body = await response.Content.ReadFromJsonAsync<BookStatsResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Assert.That(body).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(body!.Chapters).IsNotEmpty();
            await Assert.That(body.TotalWordCount).IsGreaterThan(0);
            await Assert.That(body.Chapters.Sum(c => c.WordCount)).IsEqualTo(body.TotalWordCount);
            await Assert.That(body.Chapters.All(c => c.ChapterNumber > 0)).IsTrue();
        }
    }

    // ---- helpers ----

    private sealed record PageInfo(string Key, int WordCount);

    private static PageInfo GetLargestPage() => new("reading-test-large", PageWordCounts["reading-test-large"]);

    private static PageInfo GetSmallestPage() => new("reading-test-small", PageWordCounts["reading-test-small"]);

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    private static object Interval(string pageKey, int wordsRead, int activeSeconds) =>
        new { pageKey, wordsRead, activeSeconds, completed = false };

    private static Task<HttpResponseMessage> PostSessionAsync(HttpClient client, params object[] intervals) =>
        client.PostAsJsonAsync("/api/reading/session", new { intervals });

    private async Task<string> CreateUserWithBaselineProfileAsync()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-test");
        await SeedProfileAsync(userId, BaselineWords, BaselineSeconds);
        return userId;
    }

    private async Task SeedProfileAsync(string userId, long words, long seconds) =>
        await InServiceScopeAsync(async services =>
        {
            EssentialCSharpWebContext context = services.GetRequiredService<EssentialCSharpWebContext>();
            context.UserReadingProfiles.Add(new UserReadingProfile
            {
                UserId = userId,
                TotalWordsRead = words,
                TotalActiveSeconds = seconds,
                UpdatedAtUtc = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        });

    private async Task<UserReadingProfile> GetProfileAsync(string userId) =>
        await InServiceScopeAsync(services =>
            services.GetRequiredService<EssentialCSharpWebContext>()
                .UserReadingProfiles.AsNoTracking().SingleAsync(p => p.UserId == userId));

    private async Task AssertProfileAsync(string userId, long expectedWords, long expectedSeconds)
    {
        UserReadingProfile profile = await GetProfileAsync(userId);
        using (Assert.Multiple())
        {
            await Assert.That(profile.TotalWordsRead).IsEqualTo(expectedWords);
            await Assert.That(profile.TotalActiveSeconds).IsEqualTo(expectedSeconds);
        }
    }

    /// <summary>
    /// Creates a client signed in as <paramref name="userId"/>. The antiforgery cookie and request
    /// token are minted directly from <see cref="IAntiforgery"/> for the same principal the app would
    /// build at sign-in, so no page needs to be scraped.
    /// </summary>
    private async Task<HttpClient> CreateAuthenticatedClientAsync(string userId, bool includeAntiforgery = true)
    {
        HttpClient client = McpTestHelper.CreateClient(Factory);
        (string cookieName, string cookieValue) =
            await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);
        string cookieHeader = $"{cookieName}={cookieValue}";

        if (includeAntiforgery)
        {
            (string antiforgeryCookie, string requestToken, string headerName) = await InServiceScopeAsync(async services =>
            {
                EssentialCSharpWebUser user = await services.GetRequiredService<UserManager<EssentialCSharpWebUser>>()
                    .FindByIdAsync(userId)
                    ?? throw new InvalidOperationException($"User {userId} does not exist.");
                var context = new DefaultHttpContext
                {
                    RequestServices = services,
                    User = await services.GetRequiredService<IUserClaimsPrincipalFactory<EssentialCSharpWebUser>>()
                        .CreateAsync(user)
                };

                AntiforgeryTokenSet tokens = services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
                string antiforgeryCookieName = services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.Name
                    ?? throw new InvalidOperationException("The antiforgery cookie name is not configured.");
                string cookie = context.Response.Headers.SetCookie
                    .Select(value => value?.Split(';', 2)[0])
                    .OfType<string>()
                    .Single(value => value.StartsWith(antiforgeryCookieName + "=", StringComparison.Ordinal));
                return (
                    cookie,
                    tokens.RequestToken ?? throw new InvalidOperationException("No antiforgery request token."),
                    tokens.HeaderName ?? "RequestVerificationToken");
            });

            cookieHeader += $"; {antiforgeryCookie}";
            client.DefaultRequestHeaders.Add(headerName, requestToken);
        }

        client.DefaultRequestHeaders.Add("Cookie", cookieHeader);
        return client;
    }
}
