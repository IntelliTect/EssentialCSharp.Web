using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using EssentialCSharp.Web.Controllers;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

public class ReadingControllerTests : IntegrationTestBase
{
    [Test]
    public async Task GetBookStats_IsPublic_Returns200()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await client.GetAsync("/api/reading/book-stats");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
    }

    [Test]
    public async Task GetBookStats_ReturnsExpectedShape()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await client.GetAsync("/api/reading/book-stats");

        var body = await response.Content.ReadFromJsonAsync<BookStatsResponse>();
        await Assert.That(body).IsNotNull();
        await Assert.That(body!.TotalWordCount).IsGreaterThanOrEqualTo(0);
        await Assert.That(body.Chapters).IsNotNull();
    }

    // ---- profile (requires auth) ----

    [Test]
    public async Task GetProfile_Anonymous_Returns401()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await client.GetAsync("/api/reading/profile");
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    // ---- POST session (requires auth) ----

    [Test]
    public async Task PostSession_Anonymous_Returns401()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        var intervals = new[]
        {
            new ReadingController.ReadingIntervalDto("page1", 60, 100, false)
        };
        using HttpResponseMessage response = await client.PostAsJsonAsync("/api/reading/session", intervals);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    // ---- POST reset (requires auth) ----

    [Test]
    public async Task PostReset_Anonymous_Returns401()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await client.PostAsync("/api/reading/reset", null);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task PostSession_IntervalAboveHardCutoff_DoesNotChangePersistedProfile()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        using var request = CreateSessionRequest(
            [new ReadingController.ReadingIntervalDto("fast-page", 60, 901, false)]);

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        UserReadingProfile profile = await GetProfileAsync(userId);
        await Assert.That(profile.TotalWordsRead).IsEqualTo(1200L);
        await Assert.That(profile.TotalActiveSeconds).IsEqualTo(360L);
        await Assert.That(profile.DeriveWpm()).IsEqualTo(200.0);
        await Assert.That(await GetActivityCountAsync(userId)).IsEqualTo(0);
    }

    [Test]
    public async Task PostSession_SlowInterval_ClampsTimeInPersistedProfile()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        using var request = CreateSessionRequest(
            [new ReadingController.ReadingIntervalDto("slow-page", 300, 10, false)]);

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        UserReadingProfile profile = await GetProfileAsync(userId);
        await Assert.That(profile.TotalWordsRead).IsEqualTo(1210L);
        await Assert.That(profile.TotalActiveSeconds).IsEqualTo(372L);
        await Assert.That(
            Math.Abs(profile.DeriveWpm()!.Value - 1210.0 / (372.0 / 60.0)) < 0.001).IsTrue();

        ReadingActivity activity = await GetSingleActivityAsync(userId);
        await Assert.That(activity.PageKey).IsEqualTo("slow-page");
        await Assert.That(activity.WordsRead).IsEqualTo(10);
        await Assert.That(activity.ActiveSeconds).IsEqualTo(300);
    }

    [Test]
    public async Task PostSession_SmallSlowInterval_UsesAtLeastOneClampedSecond()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        using var request = CreateSessionRequest(
            [new ReadingController.ReadingIntervalDto("tiny-slow-page", 300, 1, false)]);

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        UserReadingProfile profile = await GetProfileAsync(userId);
        await Assert.That(profile.TotalWordsRead).IsEqualTo(1201L);
        await Assert.That(profile.TotalActiveSeconds).IsEqualTo(362L);
        ReadingActivity activity = await GetSingleActivityAsync(userId);
        await Assert.That(activity.WordsRead).IsEqualTo(1);
    }

    [Test]
    public async Task PostSession_NormalInterval_AddsUnmodifiedWordsAndTimeToPersistedProfile()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        using var request = CreateSessionRequest(
            [new ReadingController.ReadingIntervalDto("normal-page", 60, 180, true)]);

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        UserReadingProfile profile = await GetProfileAsync(userId);
        await Assert.That(profile.TotalWordsRead).IsEqualTo(1380L);
        await Assert.That(profile.TotalActiveSeconds).IsEqualTo(420L);
        await Assert.That(
            Math.Abs(profile.DeriveWpm()!.Value - 1380.0 / (420.0 / 60.0)) < 0.001).IsTrue();

        ReadingActivity activity = await GetSingleActivityAsync(userId);
        await Assert.That(activity.PageKey).IsEqualTo("normal-page");
        await Assert.That(activity.WordsRead).IsEqualTo(180);
        await Assert.That(activity.ActiveSeconds).IsEqualTo(60);
        await Assert.That(activity.Completed).IsTrue();
    }

    [Test]
    public async Task PostSession_RejectsMoreThanMaximumIntervalBatch()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient client = await CreateAuthenticatedClientAsync(userId);
        ReadingController.ReadingIntervalDto[] intervals = Enumerable
            .Range(0, 51)
            .Select(index => new ReadingController.ReadingIntervalDto($"page-{index}", 60, 100, false))
            .ToArray();
        using var request = CreateSessionRequest(intervals);

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        UserReadingProfile profile = await GetProfileAsync(userId);
        await Assert.That(profile.TotalWordsRead).IsEqualTo(1200L);
        await Assert.That(profile.TotalActiveSeconds).IsEqualTo(360L);
        await Assert.That(await GetActivityCountAsync(userId)).IsEqualTo(0);
    }

    [Test]
    public async Task PostSession_ConcurrentRequests_PreserveBothAggregateIncrements()
    {
        string userId = await CreateUserWithBaselineProfileAsync();
        using HttpClient firstClient = await CreateAuthenticatedClientAsync(userId);
        using HttpClient secondClient = await CreateAuthenticatedClientAsync(userId);
        using HttpRequestMessage firstRequest = CreateSessionRequest(
            [new ReadingController.ReadingIntervalDto("first-page", 60, 100, false)]);
        using HttpRequestMessage secondRequest = CreateSessionRequest(
            [new ReadingController.ReadingIntervalDto("second-page", 60, 100, false)]);

        Task<HttpResponseMessage> firstTask = firstClient.SendAsync(firstRequest);
        Task<HttpResponseMessage> secondTask = secondClient.SendAsync(secondRequest);
        using HttpResponseMessage firstResponse = await firstTask;
        using HttpResponseMessage secondResponse = await secondTask;

        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        UserReadingProfile profile = await GetProfileAsync(userId);
        await Assert.That(profile.TotalWordsRead).IsEqualTo(1400L);
        await Assert.That(profile.TotalActiveSeconds).IsEqualTo(480L);
        await Assert.That(await GetActivityCountAsync(userId)).IsEqualTo(2);
    }

    [Test]
    public async Task PostSession_ConcurrentFirstRequests_CreateOneProfileAndPreserveBothIncrements()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-first-session");
        using HttpClient firstClient = await CreateAuthenticatedClientAsync(userId);
        using HttpClient secondClient = await CreateAuthenticatedClientAsync(userId);
        using HttpRequestMessage firstRequest = CreateSessionRequest(
            [new ReadingController.ReadingIntervalDto("first-page", 60, 100, false)]);
        using HttpRequestMessage secondRequest = CreateSessionRequest(
            [new ReadingController.ReadingIntervalDto("second-page", 60, 100, false)]);

        Task<HttpResponseMessage> firstTask = firstClient.SendAsync(firstRequest);
        Task<HttpResponseMessage> secondTask = secondClient.SendAsync(secondRequest);
        using HttpResponseMessage firstResponse = await firstTask;
        using HttpResponseMessage secondResponse = await secondTask;

        await Assert.That(firstResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(secondResponse.StatusCode).IsEqualTo(HttpStatusCode.OK);
        UserReadingProfile profile = await GetProfileAsync(userId);
        await Assert.That(profile.TotalWordsRead).IsEqualTo(200L);
        await Assert.That(profile.TotalActiveSeconds).IsEqualTo(120L);
        await Assert.That(await GetActivityCountAsync(userId)).IsEqualTo(2);
    }

    [Test]
    public async Task GetBookStats_Returns200WithChapters()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpResponseMessage response = await client.GetAsync("/api/reading/book-stats");
        await Assert.That((int)response.StatusCode).IsEqualTo(200);

        var body = await response.Content.ReadFromJsonAsync<BookStatsResponse>();
        await Assert.That(body).IsNotNull();
    }

    // ---- DTO for deserializing book-stats response ----

    private sealed class BookStatsResponse
    {
        public int TotalWordCount { get; set; }
        public IEnumerable<ChapterInfo>? Chapters { get; set; }
    }

    private sealed class ChapterInfo
    {
        public int ChapterNumber { get; set; }
        public int WordCount { get; set; }
    }

    private async Task<string> CreateUserWithBaselineProfileAsync()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-test");
        await InServiceScopeAsync(async services =>
        {
            EssentialCSharpWebContext context =
                services.GetRequiredService<EssentialCSharpWebContext>();
            context.UserReadingProfiles.Add(new UserReadingProfile
            {
                UserId = userId,
                TotalWordsRead = 1200,
                TotalActiveSeconds = 360
            });
            await context.SaveChangesAsync();
        });
        return userId;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string userId)
    {
        HttpClient client = McpTestHelper.CreateClient(Factory);
        (string cookieName, string cookieValue) =
            await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);
        client.DefaultRequestHeaders.Add("Cookie", $"{cookieName}={cookieValue}");

        using HttpResponseMessage pageResponse = await client.GetAsync("/about");
        string pageContent = await pageResponse.Content.ReadAsStringAsync();
        Match requestToken = Regex.Match(
            pageContent,
            "<meta name=\"csrf-token\" content=\"([^\"]+)\"");
        if (!requestToken.Success)
        {
            throw new InvalidOperationException(
                $"The test page returned {(int)pageResponse.StatusCode} without an antiforgery token.");
        }

        string antiforgeryCookieName = Factory.Services
            .GetRequiredService<IOptions<AntiforgeryOptions>>()
            .Value.Cookie.Name
            ?? throw new InvalidOperationException("The antiforgery cookie name is not configured.");
        string antiforgeryCookie = pageResponse.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';', 2)[0])
            .Single(value => value.StartsWith(
                $"{antiforgeryCookieName}=",
                StringComparison.Ordinal));

        client.DefaultRequestHeaders.Remove("Cookie");
        client.DefaultRequestHeaders.Add(
            "Cookie",
            $"{cookieName}={cookieValue}; {antiforgeryCookie}");
        client.DefaultRequestHeaders.Add(
            "RequestVerificationToken",
            requestToken.Groups[1].Value);

        return client;
    }

    private static HttpRequestMessage CreateSessionRequest(
        IEnumerable<ReadingController.ReadingIntervalDto> intervals)
    {
        return new HttpRequestMessage(HttpMethod.Post, "/api/reading/session")
        {
            Content = JsonContent.Create(intervals)
        };
    }

    private async Task<UserReadingProfile> GetProfileAsync(string userId)
    {
        using IServiceScope scope =
            Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>()
            .UserReadingProfiles.SingleAsync(profile => profile.UserId == userId);
    }

    private async Task<int> GetActivityCountAsync(string userId)
    {
        using IServiceScope scope =
            Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>()
            .ReadingActivities.CountAsync(activity => activity.UserId == userId);
    }

    private async Task<ReadingActivity> GetSingleActivityAsync(string userId)
    {
        using IServiceScope scope =
            Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return await scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>()
            .ReadingActivities.SingleAsync(activity => activity.UserId == userId);
    }
}
