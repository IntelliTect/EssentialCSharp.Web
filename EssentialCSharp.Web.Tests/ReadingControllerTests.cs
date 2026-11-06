using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EssentialCSharp.Web.Controllers;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// End-to-end behavior of the reading API. The speed-scoring arithmetic is covered exhaustively by
/// <see cref="ReadingSpeedPolicyTests"/>; here each scoring outcome has one smoke case that proves the policy is wired in.
/// </summary>
public class ReadingControllerTests : IntegrationTestBase
{
    // Baseline profile: 1200 words in 360 seconds = 200 WPM, i.e. a slow-outlier threshold of 50 WPM.
    private const long BaselineWords = 1200;
    private const long BaselineSeconds = 360;
    private const double BaselineWpm = 200;

    private const string LargePage = ReadingTestSupport.LargePage;

    // The content package is a placeholder in many environments, so word counts and reading options are fixed
    // (see ReadingTestSupport).
    protected override void ConfigureTestServices(IServiceCollection services) =>
        ReadingTestSupport.UseFakes(services, new ReadingTestSupport.FakeWordCountService());

    private static ReadingOptions Options => ReadingTestSupport.Options;

    // ---- authentication ----

    [Test]
    [Arguments("GET", "/api/reading/profile")]
    [Arguments("POST", "/api/reading/session")]
    [Arguments("POST", "/api/reading/reset")]
    public async Task ProtectedEndpoints_Anonymous_Return401WithoutRedirect(string method, string url)
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
        {
            request.Content = JsonBody($$"""{"intervals":[{"pageKey":"{{LargePage}}","wordsRead":10,"activeSeconds":10}]}""");
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
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId, includeAntiforgery: false);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonBody($$"""{"intervals":[{"pageKey":"{{LargePage}}","wordsRead":10,"activeSeconds":10}]}""")
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

    // ---- POST session: one smoke case per scoring outcome (the math lives in ReadingSpeedPolicyTests) ----

    [Test]
    public async Task PostSession_NormalInterval_AddsWordsAndTimeAndReturnsUpdatedProfile()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(client, Interval(LargePage, 100, 60));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        using (Assert.Multiple())
        {
            await Assert.That(body.GetProperty("totalWordsRead").GetInt64()).IsEqualTo(1300L);
            await Assert.That(body.GetProperty("totalActiveSeconds").GetInt64()).IsEqualTo(420L);
            // 1300 words in 420 s.
            await Assert.That(body.GetProperty("wpm").GetDouble()).IsEqualTo(185.714).Within(0.001);
        }
        await AssertProfileAsync(userId, 1300, 420);
    }

    [Test]
    public async Task PostSession_VerySlowInterval_IsCreditedAtTheSlowThresholdSpeed()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        // 50 words in 1800 s is far below the reader's 50 WPM threshold; at that speed 50 words take 60 s.
        using HttpResponseMessage response = await PostSessionAsync(client, Interval(LargePage, 50, 1800));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(userId, 1250, 420);
    }

    [Test]
    public async Task PostSession_VerySlowIntervalForColdStartUser_IsCreditedInFull()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-cold");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        // With no reading history there is no reference speed, so nothing counts as a slow outlier.
        using HttpResponseMessage response = await PostSessionAsync(client, Interval(LargePage, 50, 1800));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(userId, 50, 1800);
    }

    [Test]
    public async Task PostSession_ImplausiblyFastInterval_IsIgnoredAndStillSucceeds()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        // 301 words in 20 s is 903 WPM, above the 900 WPM cutoff.
        using HttpResponseMessage response = await PostSessionAsync(client, Interval(LargePage, 301, 20));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_AllIntervalsIgnoredForUserWithoutProfile_ReturnsZerosAndCreatesNoRow()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-ignored");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(
            client, Interval(LargePage, 301, 20), Interval(LargePage, 600, 20));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>();
        using (Assert.Multiple())
        {
            await Assert.That(body.GetProperty("totalWordsRead").GetInt64()).IsEqualTo(0L);
            await Assert.That(body.GetProperty("totalActiveSeconds").GetInt64()).IsEqualTo(0L);
            await Assert.That(body.GetProperty("wpm").ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(await HasProfileRowAsync(userId)).IsFalse();
        }
    }

    [Test]
    public async Task PostSession_WordsAbovePageWordCount_AreCappedAtPageWordCount()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-cap");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        // The small page has 150 words; claiming 151 is credited as 150.
        using HttpResponseMessage response = await PostSessionAsync(client, Interval(ReadingTestSupport.SmallPage, 151, 600));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(userId, 150, 600);
    }

    // ---- POST session: boundaries ----

    [Test]
    public async Task PostSession_MaxWordsRead_IsAccepted()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-maxwords");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        // 100 000 words is the DTO maximum; the page has 5000, so 5000 are credited (750 WPM over 400 s).
        using HttpResponseMessage response = await PostSessionAsync(
            client, Interval(LargePage, ReadingIntervalRequest.MaxWordsRead, 400));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(userId, 5000, 400);
    }

    [Test]
    public async Task PostSession_OneOverMaxWordsRead_IsRejectedOnWordsRead()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(
            client, Interval(LargePage, ReadingIntervalRequest.MaxWordsRead + 1, 400));

        await AssertValidationProblemAsync(response, "Intervals[0].WordsRead");
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_MaxActiveSecondsPerInterval_IsAccepted()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-maxseconds");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(
            client, Interval(LargePage, 100, Options.MaxActiveSecondsPerInterval));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(userId, 100, Options.MaxActiveSecondsPerInterval);
    }

    [Test]
    public async Task PostSession_OneOverMaxActiveSecondsPerInterval_IsRejectedOnActiveSeconds()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(
            client, Interval(LargePage, 100, Options.MaxActiveSecondsPerInterval + 1));

        await AssertValidationProblemAsync(response, nameof(ReadingIntervalRequest.ActiveSeconds));
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_AtMaximumIntervalsPerRequest_IsAccepted()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-batch-max");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        int count = Options.MaxIntervalsPerRequest;

        using HttpResponseMessage response = await PostSessionAsync(
            client, Enumerable.Repeat(Interval(LargePage, 10, 10), count).ToArray());

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(userId, 10L * count, 10L * count);
    }

    [Test]
    public async Task PostSession_OneOverMaximumIntervalsPerRequest_IsRejectedAndDoesNotChangeProfile()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(
            client, Enumerable.Repeat(Interval(LargePage, 10, 10), Options.MaxIntervalsPerRequest + 1).ToArray());

        await AssertValidationProblemAsync(response, nameof(ReadingSessionRequest.Intervals));
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_NoIntervals_IsRejected()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(client);

        await AssertValidationProblemAsync(response, nameof(ReadingSessionRequest.Intervals));
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_DeclaresA32KiBRequestSizeLimit()
    {
        // The in-memory TestServer does not enforce Kestrel's body-size feature, so a real 413 cannot be provoked here;
        // pin the endpoint metadata that Kestrel enforces instead.
        System.Reflection.MethodInfo action = typeof(ReadingController).GetMethod(nameof(ReadingController.PostSession))!;
        var limit = (Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute?)Attribute.GetCustomAttribute(
            action, typeof(Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute));

        await Assert.That(limit).IsNotNull();
        await Assert.That(((Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata)limit!).MaxRequestBodySize).IsEqualTo(32L * 1024);
    }

    [Test]
    public async Task PostSession_PageKeyOfMaximumLength_IsAccepted()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-key-max");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(
            client, Interval(ReadingTestSupport.MaxLengthPageKey, 10, 10));

        await Assert.That(ReadingTestSupport.MaxLengthPageKey.Length).IsEqualTo(ReadingIntervalRequest.PageKeyMaxLength);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(userId, 10, 10);
    }

    [Test]
    public async Task PostSession_PageKeyOneOverMaximumLength_IsRejectedOnPageKey()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(
            client, Interval(new string('k', ReadingIntervalRequest.PageKeyMaxLength + 1), 10, 10));

        await AssertValidationProblemAsync(response, "Intervals[0].PageKey");
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
    [Arguments("null interval entry", """{"intervals":[null]}""")]
    [Arguments("zero words", """{"intervals":[{"pageKey":"{KEY}","wordsRead":0,"activeSeconds":10}]}""")]
    [Arguments("negative words", """{"intervals":[{"pageKey":"{KEY}","wordsRead":-1,"activeSeconds":10}]}""")]
    [Arguments("zero seconds", """{"intervals":[{"pageKey":"{KEY}","wordsRead":10,"activeSeconds":0}]}""")]
    [Arguments("negative seconds", """{"intervals":[{"pageKey":"{KEY}","wordsRead":10,"activeSeconds":-1}]}""")]
    [Arguments("int.MaxValue words", """{"intervals":[{"pageKey":"{KEY}","wordsRead":2147483647,"activeSeconds":10}]}""")]
    [Arguments("int.MaxValue seconds", """{"intervals":[{"pageKey":"{KEY}","wordsRead":10,"activeSeconds":2147483647}]}""")]
    [Arguments("missing page key", """{"intervals":[{"wordsRead":10,"activeSeconds":10}]}""")]
    [Arguments("empty page key", """{"intervals":[{"pageKey":"","wordsRead":10,"activeSeconds":10}]}""")]
    [Arguments("one bad interval among good ones", """{"intervals":[{"pageKey":"{KEY}","wordsRead":10,"activeSeconds":10},{"pageKey":"{KEY}","wordsRead":0,"activeSeconds":10}]}""")]
    public async Task PostSession_InvalidInput_IsRejectedAndLeavesProfileUnchanged(string scenario, string bodyTemplate)
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reading/session")
        {
            Content = JsonBody(bodyTemplate.Replace("{KEY}", LargePage, StringComparison.Ordinal))
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(scenario);
        string raw = await response.Content.ReadAsStringAsync();
        bool hasErrors = false;
        try
        {
            using JsonDocument problem = JsonDocument.Parse(raw);
            hasErrors = problem.RootElement.TryGetProperty("errors", out _);
        }
        catch (JsonException)
        {
        }

        await Assert.That(hasErrors).IsTrue().Because($"{scenario}: expected a validation problem with an errors member, got {raw}");
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_UnknownPageKey_IsRejectedWithProblemDetailsOnPageKey()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(client, Interval("no-such-page", 10, 10));

        await AssertValidationProblemAsync(response, nameof(ReadingIntervalRequest.PageKey));
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_KnownPageWithZeroWords_IsTreatedLikeAnUnknownPage()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        // A page with no countable prose cannot be read, so the service rejects it as it does an unknown key.
        using HttpResponseMessage response = await PostSessionAsync(client, Interval(ReadingTestSupport.EmptyPage, 10, 10));

        await AssertValidationProblemAsync(response, nameof(ReadingIntervalRequest.PageKey));
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_ValidIntervalAlongsideUnknownPageKey_RejectsTheWholeBatch()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(
            client, Interval(LargePage, 100, 60), Interval("no-such-page", 10, 10));

        await AssertValidationProblemAsync(response, nameof(ReadingIntervalRequest.PageKey));
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    // ---- user isolation ----

    [Test]
    public async Task PostSession_UpdatesOnlyTheAuthenticatedUsersProfile()
    {
        string callerId = await CreateUserWithBaselineProfileAsync();
        string otherId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(callerId);

        using HttpResponseMessage response = await PostSessionAsync(client, Interval(LargePage, 50, 30));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await AssertProfileAsync(callerId, BaselineWords + 50, BaselineSeconds + 30);
        await AssertProfileAsync(otherId, BaselineWords, BaselineSeconds);
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
        await Assert.That(await HasProfileRowAsync(callerId)).IsFalse();
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

    // ---- rate limiting ----

    [Test]
    [Arguments("GET", "/api/reading/profile")]
    [Arguments("POST", "/api/reading/session")]
    [Arguments("POST", "/api/reading/reset")]
    public async Task WriteEndpoint_IsRateLimitedPerUser(string method, string url)
    {
        // Does not depend on the exact permit count: keep calling until the first 429, up to a ceiling.
        const int ceiling = 100;
        string limitedUser = await McpTestHelper.CreateUserAsync(Factory, "reading-limited");
        string otherUser = await McpTestHelper.CreateUserAsync(Factory, "reading-unlimited");
        using HttpClient limitedClient = await CreateAuthenticatedClientAsync(limitedUser);
        using HttpClient otherClient = await CreateAuthenticatedClientAsync(otherUser);

        var statuses = new List<HttpStatusCode>();
        while (statuses.Count < ceiling && !statuses.Contains(HttpStatusCode.TooManyRequests))
        {
            using HttpResponseMessage response = await SendAsync(limitedClient, method, url);
            statuses.Add(response.StatusCode);
        }
        using HttpResponseMessage otherUserResponse = await SendAsync(otherClient, method, url);

        using (Assert.Multiple())
        {
            await Assert.That(statuses).Contains(HttpStatusCode.TooManyRequests).Because($"no 429 within {ceiling} requests");
            await Assert.That(statuses.Take(statuses.Count - 1)).DoesNotContain(s => s != HttpStatusCode.OK && s != HttpStatusCode.NoContent);
            await Assert.That(otherUserResponse.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
        }
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string method, string url) =>
        method == "GET"
            ? client.GetAsync(url)
            : url.EndsWith("/session", StringComparison.Ordinal)
                ? PostSessionAsync(client, Interval(LargePage, 10, 10))
                : client.PostAsync(url, null);

    // ---- concurrency ----

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PostSession_ConcurrentRequests_SumCorrectly(bool userAlreadyHasProfile)
    {
        // Fewer simultaneous requests than the per-user write limit, so none is rate limited.
        const int parallelRequests = 10;
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

            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<HttpStatusCode>[] requests = clients.Select(client => Task.Run(async () =>
            {
                await gate.Task;
                using HttpResponseMessage response = await PostSessionAsync(
                    client, Interval(LargePage, wordsPerRequest, secondsPerRequest));
                return response.StatusCode;
            })).ToArray();

            gate.SetResult();
            HttpStatusCode[] statuses = await Task.WhenAll(requests);

            await Assert.That(statuses).IsEquivalentTo(Enumerable.Repeat(HttpStatusCode.OK, parallelRequests));
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
    public async Task GetBookStats_IsPublicCacheableAndReportsChaptersAndTotal()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();

        using HttpResponseMessage response = await client.GetAsync("/api/reading/book-stats");

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        BookStatsResponse? body = await response.Content.ReadFromJsonAsync<BookStatsResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await Assert.That(body).IsNotNull();
        using (Assert.Multiple())
        {
            await Assert.That(body!.TotalWordCount).IsEqualTo(5150);
            await Assert.That(body.Chapters.Select(c => (c.ChapterNumber, c.WordCount)))
                .IsEquivalentTo(new[] { (1, 3000), (2, 2150) });
            await Assert.That(response.Headers.CacheControl?.Public).IsTrue();
            await Assert.That(response.Headers.CacheControl?.MaxAge).IsEqualTo(TimeSpan.FromHours(1));
        }
    }

    // ---- personal data ----

    [Test]
    public async Task DownloadPersonalData_IncludesReadingTotalsForUserWithAProfile()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        string otherUserId = await McpTestHelper.CreateUserAsync(Factory, "reading-export-none");
        Dictionary<string, string> withProfile = await DownloadPersonalDataAsync(userId);
        Dictionary<string, string> withoutProfile = await DownloadPersonalDataAsync(otherUserId);

        using (Assert.Multiple())
        {
            await Assert.That(withProfile["Reading total words read"]).IsEqualTo("1200");
            await Assert.That(withProfile["Reading total active seconds"]).IsEqualTo("360");
            await Assert.That(withoutProfile.Keys.Where(k => k.StartsWith("Reading", StringComparison.Ordinal))).IsEmpty();
        }
    }

    private async Task<Dictionary<string, string>> DownloadPersonalDataAsync(string userId)
    {
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        using HttpResponseMessage response = await client.PostAsync("/Identity/Account/Manage/DownloadPersonalData", null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<Dictionary<string, string>>()
            ?? throw new InvalidOperationException("The personal data download was empty.");
    }

    // ---- helpers ----

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");

    private static object Interval(string pageKey, int wordsRead, int activeSeconds) =>
        new { pageKey, wordsRead, activeSeconds };

    private static Task<HttpResponseMessage> PostSessionAsync(HttpClient client, params object[] intervals) =>
        client.PostAsJsonAsync("/api/reading/session", new { intervals });

    /// <summary>Asserts a 400 validation problem whose <c>errors</c> object has exactly the expected key (message text is not asserted).</summary>
    private static async Task AssertValidationProblemAsync(HttpResponseMessage response, string expectedErrorKey)
    {
        string raw = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(raw);
        string[] keys = document.RootElement.TryGetProperty("errors", out JsonElement errors)
            ? errors.EnumerateObject().Select(p => p.Name).ToArray()
            : [];
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest).Because(raw);
            await Assert.That(response.Content.Headers.ContentType?.MediaType).IsEqualTo("application/problem+json");
            await Assert.That(keys).IsEquivalentTo(new[] { expectedErrorKey }).Because(raw);
        }
    }

    private async Task<string> CreateUserWithBaselineProfileAsync()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-test");
        await SeedProfileAsync(userId, BaselineWords, BaselineSeconds);
        return userId;
    }

    private Task SeedProfileAsync(string userId, long words, long seconds) =>
        ReadingTestSupport.SeedProfileAsync(Factory.Services, userId, words, seconds);

    private async Task<bool> HasProfileRowAsync(string userId) =>
        await InServiceScopeAsync(services =>
            services.GetRequiredService<EssentialCSharpWebContext>()
                .UserReadingProfiles.AnyAsync(p => p.UserId == userId));

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

    /// <summary>Creates a client signed in as <paramref name="userId"/>, with the antiforgery cookie and header token the app expects.</summary>
    private async Task<HttpClient> CreateAuthenticatedClientAsync(string userId, bool includeAntiforgery = true)
    {
        HttpClient client = McpTestHelper.CreateClient(Factory);
        (string cookieHeader, string headerName, string token) =
            await ReadingTestSupport.CreateAuthenticatedCredentialsAsync(Factory, userId, includeAntiforgery);
        if (includeAntiforgery)
        {
            client.DefaultRequestHeaders.Add(headerName, token);
        }

        client.DefaultRequestHeaders.Add("Cookie", cookieHeader);
        return client;
    }
}
