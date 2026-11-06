using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EssentialCSharp.Web.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// The layout hands the reading-time client its data through <c>window.READING_CONTEXT</c> (content pages only)
/// and a <c>csrf-token</c> meta tag (signed-in users only). Values are checked against a fake word-count service
/// with distinct numbers per field, so swapped or zeroed fields fail.
/// </summary>
public partial class ReadingContextRenderingTests : IntegrationTestBase
{
    private const string ContentPage = "/hello-world";

    private static readonly string[] ExpectedFields =
    [
        "isAuthenticated", "pageKey", "pageWords", "chapterWords", "wordsBeforePage", "chapterStartWords",
        "bookWords", "wpm", "defaultWpm", "maxCreditWpm", "idleThresholdSeconds", "periodicFlushActiveSeconds"
    ];

    protected override void ConfigureTestServices(IServiceCollection services) =>
        ReadingTestSupport.UseFakes(services, new ReadingTestSupport.FakeWordCountService());

    [Test]
    public async Task AnonymousContentPage_RendersEveryReadingContextFieldWithItsValue()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();

        using JsonDocument context = ParseReadingContext(await GetOkHtmlAsync(client, ContentPage));
        JsonElement root = context.RootElement;

        using (Assert.Multiple())
        {
            await Assert.That(root.EnumerateObject().Select(p => p.Name)).IsEquivalentTo(ExpectedFields);
            await Assert.That(root.GetProperty("isAuthenticated").GetBoolean()).IsFalse();
            await Assert.That(root.GetProperty("pageKey").GetString()).IsEqualTo("hello-world");
            await Assert.That(root.GetProperty("pageWords").GetInt32()).IsEqualTo(1234);
            await Assert.That(root.GetProperty("chapterWords").GetInt32()).IsEqualTo(3000);
            await Assert.That(root.GetProperty("wordsBeforePage").GetInt32()).IsEqualTo(400);
            await Assert.That(root.GetProperty("chapterStartWords").GetInt32()).IsEqualTo(100);
            await Assert.That(root.GetProperty("bookWords").GetInt32()).IsEqualTo(5150);
            await Assert.That(root.GetProperty("wpm").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
    }

    [Test]
    public async Task ContentPage_RendersTheConfiguredReadingOptionsAsClientConstants()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        ReadingOptions configured = ReadingTestSupport.Options;

        using JsonDocument context = ParseReadingContext(await GetOkHtmlAsync(client, ContentPage));
        JsonElement root = context.RootElement;

        // The configured values are deliberately not the defaults (210/810/310/65), so a hard-coded constant fails.
        using (Assert.Multiple())
        {
            await Assert.That(root.GetProperty("defaultWpm").GetInt32()).IsEqualTo(configured.DefaultWpm).And.IsEqualTo(210);
            await Assert.That(root.GetProperty("maxCreditWpm").GetInt32()).IsEqualTo(configured.MaxCreditWpm).And.IsEqualTo(810);
            await Assert.That(root.GetProperty("idleThresholdSeconds").GetInt32()).IsEqualTo(configured.IdleThresholdSeconds).And.IsEqualTo(310);
            await Assert.That(root.GetProperty("periodicFlushActiveSeconds").GetInt32()).IsEqualTo(configured.PeriodicFlushActiveSeconds).And.IsEqualTo(65);
        }
    }

    [Test]
    [Arguments("/about")]
    [Arguments("/Identity/Account/Login")]
    public async Task NonContentPage_DoesNotRenderReadingContext(string path)
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();

        string html = await GetOkHtmlAsync(client, path);

        await Assert.That(html).DoesNotContain("READING_CONTEXT");
    }

    [Test]
    public async Task AnonymousVisitor_GetsNoCsrfMetaAndNoAntiforgeryCookie()
    {
        using HttpClient client = CreateClientWithoutAutoRedirect();
        string antiforgeryCookieName = ReadingTestSupport.AntiforgeryCookieName(Factory.Services);

        using HttpResponseMessage response = await client.GetAsync(ContentPage);
        string html = await response.Content.ReadAsStringAsync();
        response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? cookies);

        using (Assert.Multiple())
        {
            await Assert.That(html).DoesNotContain("name=\"csrf-token\"");
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
        using JsonDocument context = ParseReadingContext(html);

        using (Assert.Multiple())
        {
            await Assert.That(context.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsTrue();
            await Assert.That(context.RootElement.GetProperty("wpm").GetInt32()).IsEqualTo(200);
            await Assert.That(CsrfToken(html)).IsNotNull().And.IsNotEmpty();
        }
    }

    [Test]
    public async Task SignedInUser_WpmIsRoundedToTheNearestWholeNumber()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-context-round");
        // 1300 words in 420 seconds is 185.71 WPM, which rounds up (truncating would give 185).
        await ReadingTestSupport.SeedProfileAsync(Factory.Services, userId, 1300, 420);

        using JsonDocument context = ParseReadingContext(await GetSignedInHtmlAsync(userId));

        await Assert.That(context.RootElement.GetProperty("wpm").GetInt32()).IsEqualTo(186);
    }

    [Test]
    public async Task SignedInUserWithoutReadingHistory_GetsNullWpm()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-context-new");

        using JsonDocument context = ParseReadingContext(await GetSignedInHtmlAsync(userId));

        using (Assert.Multiple())
        {
            await Assert.That(context.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsTrue();
            await Assert.That(context.RootElement.GetProperty("wpm").ValueKind).IsEqualTo(JsonValueKind.Null);
        }
    }

    private async Task<string> GetSignedInHtmlAsync(string userId)
    {
        (string cookieName, string cookieValue) = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpRequestMessage request = new(HttpMethod.Get, ContentPage);
        McpTestHelper.AddCookie(request, cookieName, cookieValue);

        using HttpResponseMessage response = await client.SendAsync(request);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
    }

    internal static JsonDocument ParseReadingContext(string html)
    {
        Match match = ReadingContextScript().Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException("The page has no window.READING_CONTEXT assignment.");
        }

        return JsonDocument.Parse(match.Groups[1].Value);
    }

    internal static string? CsrfToken(string html)
    {
        Match match = CsrfMeta().Match(html);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"window\.READING_CONTEXT\s*=\s*(\{.*?\});\s*</script>", RegexOptions.Singleline)]
    private static partial Regex ReadingContextScript();

    [GeneratedRegex("<meta name=\"csrf-token\" content=\"([^\"]*)\"")]
    private static partial Regex CsrfMeta();

    internal static async Task<string> GetOkHtmlAsync(HttpClient client, string path)
    {
        using HttpResponseMessage response = await GetFollowingGetRedirectsAsync(client, path);
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        return await response.Content.ReadAsStringAsync();
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

        using JsonDocument context = ReadingContextRenderingTests.ParseReadingContext(
            await ReadingContextRenderingTests.GetOkHtmlAsync(client, "/hello-world"));
        JsonElement root = context.RootElement;

        using (Assert.Multiple())
        {
            await Assert.That(root.GetProperty("pageKey").GetString()).IsEqualTo("hello-world");
            await Assert.That(root.GetProperty("pageWords").GetInt32()).IsEqualTo(0);
            await Assert.That(root.GetProperty("chapterWords").GetInt32()).IsEqualTo(0);
            await Assert.That(root.GetProperty("wordsBeforePage").GetInt32()).IsEqualTo(0);
            await Assert.That(root.GetProperty("chapterStartWords").GetInt32()).IsEqualTo(0);
            await Assert.That(root.GetProperty("bookWords").GetInt32()).IsEqualTo(5150);
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
        (string cookieName, string cookieValue) = await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);
        using HttpClient client = CreateClientWithoutAutoRedirect();
        using HttpRequestMessage request = new(HttpMethod.Get, "/hello-world");
        McpTestHelper.AddCookie(request, cookieName, cookieValue);

        using HttpResponseMessage response = await client.SendAsync(request);
        string html = await response.Content.ReadAsStringAsync();

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        using JsonDocument context = ReadingContextRenderingTests.ParseReadingContext(html);
        using (Assert.Multiple())
        {
            await Assert.That(context.RootElement.GetProperty("isAuthenticated").GetBoolean()).IsTrue();
            await Assert.That(context.RootElement.GetProperty("wpm").ValueKind).IsEqualTo(JsonValueKind.Null);
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
