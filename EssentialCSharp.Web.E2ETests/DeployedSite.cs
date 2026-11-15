using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

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

    /// <summary>The configured base URL; throws a descriptive error when it is missing or malformed.</summary>
    public static Uri RequiredBaseUrl => BaseUrl ?? throw new InvalidOperationException(
        $"{BaseUrlVariable} must be an absolute http(s) URL, e.g. https://dev.essentialcsharp.com (got '{s_rawBaseUrl}').");

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
    /// One retry for transient failures, so a single blip (or a shared runner IP hitting a rate limit)
    /// does not fail a deployment gate. <see cref="HttpRetryStrategyOptions"/> treats 5xx, 408, 429,
    /// connection errors and per-attempt timeouts as transient and honours <c>Retry-After</c>; any other
    /// status (including the 302s and 404s tests assert on) is returned unchanged on the first attempt.
    /// </summary>
    private static readonly ResiliencePipeline<HttpResponseMessage> s_pipeline =
        new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = 1,
                BackoffType = DelayBackoffType.Constant,
                Delay = TimeSpan.FromSeconds(5),
                MaxDelay = TimeSpan.FromSeconds(30),
            })
            .AddTimeout(TimeSpan.FromSeconds(30))
            .Build();

    /// <summary>
    /// Creates a client that retries transient failures once, does not follow redirects (so tests can
    /// assert them), and keeps cookies (needed for antiforgery-protected form posts).
    /// </summary>
    public static HttpClient CreateClient()
    {
        Uri baseUrl = RequiredBaseUrl;
        var handler = new ResilienceHandler(s_pipeline)
        {
            InnerHandler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                CookieContainer = new CookieContainer(),
                UseCookies = true,
            },
        };
        var client = new HttpClient(handler, disposeHandler: true)
        {
            BaseAddress = baseUrl,
            // Covers both attempts plus the retry delay; each attempt is limited to 30s by the pipeline.
            Timeout = TimeSpan.FromSeconds(100),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EssentialCSharp-SmokeTests/1.0");
        return client;
    }
}

/// <summary>Skips a test unless a deployed site was requested via <c>E2E_BASE_URL</c> / <c>E2E_EXPECTED_COMMIT</c>.</summary>
public sealed class RequiresDeployedSiteAttribute()
    : SkipAttribute($"Set {DeployedSite.BaseUrlVariable} to run tests against a deployed site.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!DeployedSite.IsRequested);
}
