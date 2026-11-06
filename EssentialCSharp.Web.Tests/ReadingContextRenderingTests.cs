using System.Net;
using System.Text.Json;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// The layout hands the reading-time client its data through <c>window.READING_CONTEXT</c> (content pages only)
/// and a <c>csrf-token</c> meta tag (signed-in users only). Values are checked against a fake word-count service
/// with distinct numbers per field, so swapped or zeroed fields fail.
/// </summary>
public class ReadingContextRenderingTests : IntegrationTestBase
{
    private const string ContentPage = "/" + ReadingTestSupport.ContentPageKey;

    private readonly ReadingOptions _options = ReadingTestSupport.CreateOptions();

    protected override void ConfigureTestServices(IServiceCollection services) =>
        ReadingTestSupport.UseFakes(services, new ReadingTestSupport.FakeWordCountService(), _options);

    [Test]
    public async Task AnonymousContentPage_RendersThePageStatisticsAndNoPersonalWpm()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        PageWordStats expected = ReadingTestSupport.ContentPageStats;

        using JsonDocument context = await GetReadingContextAsync(client, ContentPage);
        JsonElement root = context.RootElement;

        using (Assert.Multiple())
        {
            await Assert.That(ReadingTestSupport.Field(root, "isAuthenticated").GetBoolean()).IsFalse();
            await Assert.That(ReadingTestSupport.Field(root, "pageKey").GetString()).IsEqualTo(ReadingTestSupport.ContentPageKey);
            await Assert.That(ReadingTestSupport.Field(root, "pageWords").GetInt32()).IsEqualTo(expected.PageWords);
            await Assert.That(ReadingTestSupport.Field(root, "chapterWords").GetInt32()).IsEqualTo(expected.ChapterWords);
            await Assert.That(ReadingTestSupport.Field(root, "wordsBeforePage").GetInt32()).IsEqualTo(expected.WordsBeforePage);
            await Assert.That(ReadingTestSupport.Field(root, "chapterStartWords").GetInt32()).IsEqualTo(expected.ChapterStartWords);
            await Assert.That(ReadingTestSupport.Field(root, "bookWords").GetInt32()).IsEqualTo(expected.BookWords);
            await Assert.That(ReadingTestSupport.Field(root, "wpm").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task ContentPage_RendersTheConfiguredReadingOptionsAsClientConstants()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        var defaults = new ReadingOptions();

        using JsonDocument context = await GetReadingContextAsync(client, ContentPage);
        JsonElement root = context.RootElement;

        using (Assert.Multiple())
        {
            // Guard: the test options must differ from the defaults, otherwise a hard-coded constant would pass.
            await Assert.That(_options.DefaultWpm).IsNotEqualTo(defaults.DefaultWpm);
            await Assert.That(_options.MaxCreditWpm).IsNotEqualTo(defaults.MaxCreditWpm);
            await Assert.That(_options.IdleThresholdSeconds).IsNotEqualTo(defaults.IdleThresholdSeconds);
            await Assert.That(_options.PeriodicFlushActiveSeconds).IsNotEqualTo(defaults.PeriodicFlushActiveSeconds);

            await Assert.That(ReadingTestSupport.Field(root, "defaultWpm").GetInt32()).IsEqualTo(_options.DefaultWpm);
            await Assert.That(ReadingTestSupport.Field(root, "maxCreditWpm").GetInt32()).IsEqualTo(_options.MaxCreditWpm);
            await Assert.That(ReadingTestSupport.Field(root, "idleThresholdSeconds").GetInt32()).IsEqualTo(_options.IdleThresholdSeconds);
            await Assert.That(ReadingTestSupport.Field(root, "periodicFlushActiveSeconds").GetInt32()).IsEqualTo(_options.PeriodicFlushActiveSeconds);
        }
    }

    [Test]
    [Arguments("/about")]
    [Arguments("/Identity/Account/Login")]
    public async Task NonContentPage_RendersNormallyWithoutReadingContext(string path)
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();

        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, path);
        string html = await ReadingTestSupport.ReadOkHtmlAsync(response);

        using (Assert.Multiple())
        {
            await Assert.That(ReadingTestSupport.PageTitle(html)).IsNotNull().And.IsNotEmpty()
                .Because("the page itself must render");
            await Assert.That(ReadingTestSupport.TryParseReadingContext(html)).IsNull();
        }
    }

    [Test]
    public async Task AnonymousVisitor_GetsNoCsrfMetaAndNoAntiforgeryCookie()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        string antiforgeryCookieName = ReadingTestSupport.AntiforgeryCookieName(Factory.Services);

        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, ContentPage);
        string html = await ReadingTestSupport.ReadOkHtmlAsync(response);
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies);

        using (Assert.Multiple())
        {
            await Assert.That(ReadingTestSupport.CsrfToken(html)).IsNull();
            await Assert.That(cookies ?? []).DoesNotContain(c => c.StartsWith(antiforgeryCookieName + "=", StringComparison.Ordinal));
        }
    }

    [Test]
    public async Task SignedInUser_GetsCsrfTokenAndTheirDerivedWpmInReadingContext()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-context");
        // 1200 words in 360 seconds is 200 WPM.
        await ReadingTestSupport.SeedProfileAsync(Factory.Services, userId, 1200, 360);

        string html = await GetSignedInHtmlAsync(userId);
        using JsonDocument context = ReadingTestSupport.ParseReadingContext(html);

        using (Assert.Multiple())
        {
            await Assert.That(ReadingTestSupport.Field(context.RootElement, "isAuthenticated").GetBoolean()).IsTrue();
            await Assert.That(ReadingTestSupport.Field(context.RootElement, "wpm").GetInt32()).IsEqualTo(200);
            await Assert.That(ReadingTestSupport.CsrfToken(html)).IsNotNull().And.IsNotEmpty();
        }
    }

    [Test]
    public async Task SignedInUser_WpmIsRoundedToTheNearestWholeNumber()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-context-round");
        // 1300 words in 420 seconds is 185.71 WPM, which rounds up (truncating would give 185).
        await ReadingTestSupport.SeedProfileAsync(Factory.Services, userId, 1300, 420);

        using JsonDocument context = ReadingTestSupport.ParseReadingContext(await GetSignedInHtmlAsync(userId));

        await Assert.That(ReadingTestSupport.Field(context.RootElement, "wpm").GetInt32()).IsEqualTo(186);
    }

    [Test]
    public async Task SignedInUserWithoutReadingHistory_GetsNullWpm()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-context-new");

        using JsonDocument context = ReadingTestSupport.ParseReadingContext(await GetSignedInHtmlAsync(userId));

        using (Assert.Multiple())
        {
            await Assert.That(ReadingTestSupport.Field(context.RootElement, "isAuthenticated").GetBoolean()).IsTrue();
            await Assert.That(ReadingTestSupport.Field(context.RootElement, "wpm").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
    }

    private static async Task<JsonDocument> GetReadingContextAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, path);
        return ReadingTestSupport.ParseReadingContext(await ReadingTestSupport.ReadOkHtmlAsync(response));
    }

    private async Task<string> GetSignedInHtmlAsync(string userId)
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpRequestMessage request = new(HttpMethod.Get, ContentPage);
        request.Headers.Add("Cookie", await ReadingTestSupport.CreateSignedInCookieHeaderAsync(Factory, userId));

        using HttpResponseMessage response = await client.SendAsync(request);
        return await ReadingTestSupport.ReadOkHtmlAsync(response);
    }
}

/// <summary>A content page the word-count service has no statistics for still renders a usable context.</summary>
public class ReadingContextUnknownPageTests : IntegrationTestBase
{
    protected override void ConfigureTestServices(IServiceCollection services) =>
        ReadingTestSupport.UseFakes(services, new ReadingTestSupport.FakeWordCountService(includeContentPage: false));

    [Test]
    public async Task PageWithoutStats_FallsBackToZeroPageValuesAndTheBookTotal()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();

        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, "/" + ReadingTestSupport.ContentPageKey);
        using JsonDocument context = ReadingTestSupport.ParseReadingContext(await ReadingTestSupport.ReadOkHtmlAsync(response));
        JsonElement root = context.RootElement;

        using (Assert.Multiple())
        {
            await Assert.That(ReadingTestSupport.Field(root, "pageKey").GetString()).IsEqualTo(ReadingTestSupport.ContentPageKey);
            await Assert.That(ReadingTestSupport.Field(root, "pageWords").GetInt32()).IsEqualTo(0);
            await Assert.That(ReadingTestSupport.Field(root, "chapterWords").GetInt32()).IsEqualTo(0);
            await Assert.That(ReadingTestSupport.Field(root, "wordsBeforePage").GetInt32()).IsEqualTo(0);
            await Assert.That(ReadingTestSupport.Field(root, "chapterStartWords").GetInt32()).IsEqualTo(0);
            await Assert.That(ReadingTestSupport.Field(root, "bookWords").GetInt32()).IsEqualTo(ReadingTestSupport.BookWords);
        }
    }
}

/// <summary>A reading-profile outage must not break book pages.</summary>
public class ReadingContextProfileFailureTests : IntegrationTestBase
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        ReadingTestSupport.UseFakes(services, new ReadingTestSupport.FakeWordCountService());
        services.RemoveAll<IReadingProfileService>();
        services.AddScoped<IReadingProfileService, ThrowingReadingProfileService>();
    }

    [Test]
    public async Task ProfileServiceFailure_StillRendersThePageWithNullWpm()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-context-outage");
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpRequestMessage request = new(HttpMethod.Get, "/" + ReadingTestSupport.ContentPageKey);
        request.Headers.Add("Cookie", await ReadingTestSupport.CreateSignedInCookieHeaderAsync(Factory, userId));

        using HttpResponseMessage response = await client.SendAsync(request);
        string html = await ReadingTestSupport.ReadOkHtmlAsync(response);

        using JsonDocument context = ReadingTestSupport.ParseReadingContext(html);
        using (Assert.Multiple())
        {
            await Assert.That(ReadingTestSupport.Field(context.RootElement, "isAuthenticated").GetBoolean()).IsTrue();
            await Assert.That(ReadingTestSupport.Field(context.RootElement, "wpm").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
    }

    private sealed class ThrowingReadingProfileService : IReadingProfileService
    {
        public Task<RecordSessionResult> RecordSessionAsync(string userId, IReadOnlyList<ReadingInterval> intervals, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("profile store unavailable");

        public Task<ReadingProfileSummary> GetProfileAsync(string userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("profile store unavailable");

        public Task ResetAsync(string userId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("profile store unavailable");
    }
}
