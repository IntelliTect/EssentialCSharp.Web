using System.Net;

namespace EssentialCSharp.Web.E2ETests;

/// <summary>
/// The deployed site under test, configured through environment variables:
/// <list type="bullet">
/// <item><c>E2E_BASE_URL</c> — required, e.g. <c>https://dev.essentialcsharp.com</c>.</item>
/// <item><c>E2E_EXPECTED_COMMIT</c> — optional; when set, <c>/version</c> must report this commit.</item>
/// </list>
/// </summary>
public static class DeployedSite
{
    public const string BaseUrlVariable = "E2E_BASE_URL";
    public const string ExpectedCommitVariable = "E2E_EXPECTED_COMMIT";

    private static readonly string? s_rawBaseUrl = Environment.GetEnvironmentVariable(BaseUrlVariable);

    public static Uri? BaseUrl { get; } = ParseBaseUrl(s_rawBaseUrl);

    /// <summary>
    /// Deployed tests are skipped only when nothing asked for them. If either variable is set (as the
    /// deployment gate does), they run — so a malformed URL fails loudly instead of skipping every test
    /// and letting the gate pass without testing anything.
    /// </summary>
    public static bool IsRequested =>
        !string.IsNullOrWhiteSpace(s_rawBaseUrl) || ExpectedCommit is not null;

    internal static Uri? ParseBaseUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value.Trim().TrimEnd('/') + "/", UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri
            : null;

    public static string? ExpectedCommit { get; } =
        Environment.GetEnvironmentVariable(ExpectedCommitVariable) is { Length: > 0 } commit ? commit : null;

    /// <summary>
    /// Creates a client that does not follow redirects (so tests can assert them) and keeps cookies
    /// (needed for antiforgery-protected form posts).
    /// </summary>
    public static HttpClient CreateClient()
    {
        Uri baseUrl = BaseUrl ?? throw new InvalidOperationException(
            $"{BaseUrlVariable} must be an absolute http(s) URL, e.g. https://dev.essentialcsharp.com (got '{s_rawBaseUrl}').");
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = new CookieContainer(),
            UseCookies = true,
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseUrl,
            Timeout = TimeSpan.FromSeconds(30),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EssentialCSharp-SmokeTests/1.0");
        return client;
    }

    private static readonly TimeSpan s_maxRetryDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Sends a request, retrying once after a 429, a 5xx, a connection failure, or a timeout so a single
    /// transient blip (or a shared runner IP hitting a rate limit) does not fail a deployment gate.
    /// Real failures persist and still fail.
    /// </summary>
    public static async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        Func<HttpRequestMessage> createRequest,
        CancellationToken cancellationToken = default)
    {
        TimeSpan delay = TimeSpan.FromSeconds(5);
        try
        {
            HttpResponseMessage response = await client.SendAsync(createRequest(), cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500)
            {
                return response;
            }

            if (response.Headers.RetryAfter?.Delta is { } retryAfter)
            {
                delay = retryAfter < s_maxRetryDelay ? retryAfter : s_maxRetryDelay;
            }
            response.Dispose();
        }
        catch (Exception ex) when (ex is HttpRequestException
            || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Connection reset / DNS / TLS hiccup, or HttpClient.Timeout elapsed: retry once below.
        }

        await Task.Delay(delay, cancellationToken);
        return await client.SendAsync(createRequest(), cancellationToken);
    }

    public static Task<HttpResponseMessage> GetWithRetryAsync(HttpClient client, string relativeUrl, CancellationToken cancellationToken = default) =>
        SendWithRetryAsync(client, () => new HttpRequestMessage(HttpMethod.Get, relativeUrl), cancellationToken);
}

/// <summary>Skips a test unless a deployed site was requested via <c>E2E_BASE_URL</c> / <c>E2E_EXPECTED_COMMIT</c>.</summary>
public sealed class RequiresDeployedSiteAttribute()
    : SkipAttribute($"Set {DeployedSite.BaseUrlVariable} to run tests against a deployed site.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!DeployedSite.IsRequested);
}
