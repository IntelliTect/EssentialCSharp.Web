using System.Net;
using System.Security.Claims;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EssentialCSharp.Web.Controllers;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// End-to-end behavior of the reading API. The speed-scoring arithmetic is covered exhaustively by
/// <see cref="ReadingSpeedPolicyTests"/>; here each scoring outcome has one smoke case that proves the policy is wired in.
/// </summary>
public class ReadingControllerTests : IntegrationTestBase
{
    private const long BaselineWords = ReadingTestSupport.BaselineWords;
    private const long BaselineSeconds = ReadingTestSupport.BaselineSeconds;

    private const string LargePage = ReadingTestSupport.LargePage;

    // The content package is a placeholder in many environments, so word counts and reading options are fixed
    // (see ReadingTestSupport).
    private readonly ReadingOptions Options = ReadingTestSupport.CreateOptions();

    protected override void ConfigureTestServices(IServiceCollection services) =>
        ReadingTestSupport.UseFakes(services, new ReadingTestSupport.FakeWordCountService(), Options);

    // ---- authentication ----

    [Test]
    [Arguments("/api/reading/session")]
    [Arguments("/api/reading/reset")]
    public async Task ProtectedEndpoints_Anonymous_Return401WithoutRedirect(string url)
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonBody($$"""{"intervals":[{"pageKey":"{{LargePage}}","wordsRead":10,"activeSeconds":10}]}""")
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
            await Assert.That(response.Headers.Location).IsNull();
        }
    }

    // A missing or invalid antiforgery token is covered by ReadingAntiforgeryContractTests.

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

        await ReadingTestSupport.AssertValidationProblemAsync(response, "Intervals[0]", nameof(ReadingIntervalRequest.WordsRead));
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
            client, Interval(LargePage, 10, 10), Interval(LargePage, 100, Options.MaxActiveSecondsPerInterval + 1));

        await ReadingTestSupport.AssertValidationProblemAsync(response, "Intervals[1]", nameof(ReadingIntervalRequest.ActiveSeconds));
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

        await ReadingTestSupport.AssertValidationProblemAsync(response, nameof(ReadingSessionRequest.Intervals));
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_NullIntervalEntry_IsRejectedOnThatEntry()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reading/session")
        {
            Content = JsonBody($$"""{"intervals":[{"pageKey":"{{LargePage}}","wordsRead":10,"activeSeconds":10},null]}""")
        };

        using HttpResponseMessage response = await client.SendAsync(request);

        await ReadingTestSupport.AssertValidationProblemAsync(response, "Intervals[1]");
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_NoIntervals_IsRejected()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(client);

        await ReadingTestSupport.AssertValidationProblemAsync(response, nameof(ReadingSessionRequest.Intervals));
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_DeclaresARequestSizeLimitInItsMetadataThatFitsAMaximalLegitimateRequestButIsFarBelowTheServerDefault()
    {
        // The in-memory TestServer does not enforce Kestrel's body-size feature, so an oversized request cannot be
        // rejected here; read the limit the endpoint declares (which Kestrel enforces) from the endpoint metadata.
        EndpointDataSource endpoints = Factory.Services.GetRequiredService<EndpointDataSource>();
        long?[] declaredLimits = endpoints.Endpoints
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is { ActionName: nameof(ReadingController.PostSession) })
            .Select(e => e.Metadata.GetMetadata<IRequestSizeLimitMetadata>()?.MaxRequestBodySize)
            .Distinct()
            .ToArray();
        long? limit = declaredLimits.Length == 1 ? declaredLimits[0] : null;

        // The largest request a well-behaved client may send: the maximum number of intervals, each with a
        // maximum-length page key and the largest numeric values.
        object maximalInterval = Interval(
            ReadingTestSupport.MaxLengthPageKey, ReadingIntervalRequest.MaxWordsRead, Options.MaxActiveSecondsPerInterval);
        int maximalRequestBytes = JsonSerializer.SerializeToUtf8Bytes(
            new { intervals = Enumerable.Repeat(maximalInterval, Options.MaxIntervalsPerRequest).ToArray() }).Length;
        const long KestrelDefaultMaxRequestBodySize = 30_000_000;

        using (Assert.Multiple())
        {
            await Assert.That(limit).IsNotNull().Because("the endpoint must declare its own request size limit");
            await Assert.That(limit ?? 0).IsGreaterThanOrEqualTo(maximalRequestBytes);
            await Assert.That(limit ?? long.MaxValue).IsLessThan(KestrelDefaultMaxRequestBodySize);
        }
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

        await ReadingTestSupport.AssertValidationProblemAsync(response, "Intervals[0]", nameof(ReadingIntervalRequest.PageKey));
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

        ReadingTestSupport.ProblemResponse problem = await ReadingTestSupport.ReadProblemAsync(response);

        using (Assert.Multiple())
        {
            await Assert.That(problem.Status).IsEqualTo(HttpStatusCode.BadRequest).Because($"{scenario}: {problem}");
            await Assert.That(problem.HasErrorsMember).IsTrue().Because($"{scenario}: expected a validation problem with an errors member, got {problem}");
        }

        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_UnknownPageKey_IsRejectedWithProblemDetailsOnPageKey()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(client, Interval("no-such-page", 10, 10));

        await ReadingTestSupport.AssertValidationProblemAsync(response, "Intervals[0]", nameof(ReadingIntervalRequest.PageKey));
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_KnownPageWithZeroWords_IsTreatedLikeAnUnknownPage()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        // A page with no countable prose cannot be read, so the service rejects it as it does an unknown key.
        using HttpResponseMessage response = await PostSessionAsync(client, Interval(ReadingTestSupport.EmptyPage, 10, 10));

        await ReadingTestSupport.AssertValidationProblemAsync(response, "Intervals[0]", nameof(ReadingIntervalRequest.PageKey));
        await AssertProfileAsync(userId, BaselineWords, BaselineSeconds);
    }

    [Test]
    public async Task PostSession_ValidIntervalAlongsideUnknownPageKey_RejectsTheWholeBatch()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage response = await PostSessionAsync(
            client, Interval(LargePage, 100, 60), Interval("no-such-page", 10, 10));

        await ReadingTestSupport.AssertValidationProblemAsync(response, "Intervals[1]", nameof(ReadingIntervalRequest.PageKey));
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

    [Test]
    public async Task Reset_CalledTwice_IsIdempotent()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage first = await client.PostAsync("/api/reading/reset", null);
        using HttpResponseMessage second = await client.PostAsync("/api/reading/reset", null);

        using (Assert.Multiple())
        {
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(await HasProfileRowAsync(userId)).IsFalse();
        }
    }

    [Test]
    public async Task Reset_ThenRecording_StartsAFreshProfileWithNoReferenceSpeed()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        using HttpResponseMessage reset = await client.PostAsync("/api/reading/reset", null);
        // 50 words in 1800 s would be clamped for the baseline reader, but after a reset there is no reference speed.
        using HttpResponseMessage record = await PostSessionAsync(client, Interval(LargePage, 50, 1800));

        using (Assert.Multiple())
        {
            await Assert.That(reset.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
            await Assert.That(record.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
        await AssertProfileAsync(userId, 50, 1800);
    }

    // ---- rate limiting ----

    [Test]
    [Arguments("/api/reading/session")]
    [Arguments("/api/reading/reset")]
    public async Task AuthenticatedEndpoint_ThrottlesAUserWhoKeepsCallingWith429_WithoutAffectingOtherUsers(string url)
    {
        // The permit count is a production tuning value and is deliberately not asserted: keep calling until the
        // first 429. The ceiling is only a sanity bound that turns "never throttled" into a failure.
        const int Ceiling = 1000;
        string limitedUser = await McpTestHelper.CreateUserAsync(Factory, "reading-limited");
        string otherUser = await McpTestHelper.CreateUserAsync(Factory, "reading-unlimited");
        using HttpClient limitedClient = await CreateAuthenticatedClientAsync(limitedUser);
        using HttpClient otherClient = await CreateAuthenticatedClientAsync(otherUser);

        var statuses = new List<HttpStatusCode>();
        HttpResponseMessage? throttled = null;
        try
        {
            while (throttled is null && statuses.Count < Ceiling)
            {
                HttpResponseMessage response = await SendAsync(limitedClient, url);
                statuses.Add(response.StatusCode);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    throttled = response;
                }
                else
                {
                    response.Dispose();
                }
            }

            using HttpResponseMessage otherUserResponse = await SendAsync(otherClient, url);

            using (Assert.Multiple())
            {
                await Assert.That(throttled).IsNotNull().Because($"no 429 within {Ceiling} requests");
                await Assert.That(throttled?.Headers.RetryAfter).IsNotNull().Because("a throttled response tells the client when to retry");
                await Assert.That(statuses.Take(statuses.Count - 1))
                    .DoesNotContain(s => s != HttpStatusCode.OK && s != HttpStatusCode.NoContent)
                    .Because("every request before the throttle must succeed");
                await Assert.That(otherUserResponse.StatusCode).IsNotEqualTo(HttpStatusCode.TooManyRequests);
            }
        }
        finally
        {
            throttled?.Dispose();
        }
    }

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string url) =>
        url.EndsWith("/session", StringComparison.Ordinal)
            ? PostSessionAsync(client, Interval(LargePage, 10, 10))
            : client.PostAsync(url, null);

    // ---- profile timestamps ----

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PostSession_StampsUpdatedAtUtcWithTheTimeOfTheWrite(bool userAlreadyHasProfile)
    {
        string userId = userAlreadyHasProfile
            ? await CreateUserWithBaselineProfileAsync(updatedAtUtc: DateTime.UtcNow.AddDays(-30))
            : await McpTestHelper.CreateUserAsync(Factory, "reading-stamp");
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);

        DateTime before = DateTime.UtcNow;
        using HttpResponseMessage response = await PostSessionAsync(client, Interval(LargePage, 10, 10));
        DateTime after = DateTime.UtcNow;

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        DateTime updatedAt = DateTime.SpecifyKind((await GetProfileAsync(userId)).UpdatedAtUtc, DateTimeKind.Utc);
        using (Assert.Multiple())
        {
            await Assert.That(updatedAt).IsGreaterThanOrEqualTo(before.AddSeconds(-1));
            await Assert.That(updatedAt).IsLessThanOrEqualTo(after.AddSeconds(1));
        }
    }

    // ---- identity without a user id ----

    [Test]
    [Arguments("/api/reading/session")]
    [Arguments("/api/reading/reset")]
    public async Task AuthenticatedPrincipalWithoutUserIdClaim_IsUnauthorizedAndWritesNothing(string url)
    {
        // A signed-in principal that carries no NameIdentifier claim has no user to act for. The antiforgery token is
        // minted for the same stripped principal so the request is rejected for the missing id, not for the token.
        string victimId = await CreateUserWithBaselineProfileAsync();
        ReadingTestSupport.AntiforgeryCredentials credentials = await ReadingTestSupport.CreateSignedInAntiforgeryCredentialsAsync(
            Factory,
            victimId,
            principal => new ClaimsPrincipal(new ClaimsIdentity(
                principal.Claims.Where(c => c.Type != ClaimTypes.NameIdentifier),
                principal.Identity?.AuthenticationType)));
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonBody($$"""{"intervals":[{"pageKey":"{{LargePage}}","wordsRead":10,"activeSeconds":10}]}""")
        };
        request.Headers.Add("Cookie", credentials.CookieHeader);
        request.Headers.Add(credentials.HeaderName, credentials.RequestToken);

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await AssertProfileAsync(victimId, BaselineWords, BaselineSeconds);
    }

    // ---- concurrency ----

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task PostSession_ManyParallelRequests_AreAllAcceptedAndTheTotalsAddUp(bool userAlreadyHasProfile)
    {
        // On SQLite this proves the endpoint is wired for a burst and the final sum is right; it does not prove
        // lost-update safety, because SQLite serializes writers. That needs a real SQL Server (tracked separately).
        // The burst is kept small enough that no request is rate limited.
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

    private async Task<string> CreateUserWithBaselineProfileAsync(DateTime? updatedAtUtc = null)
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-test");
        await ReadingTestSupport.SeedProfileAsync(Factory.Services, userId, BaselineWords, BaselineSeconds, updatedAtUtc);
        return userId;
    }

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

    private Task<HttpClient> CreateAuthenticatedClientAsync(string userId, bool includeAntiforgery = true) =>
        ReadingTestSupport.CreateSignedInClientAsync(Factory, userId, includeAntiforgery);
}
