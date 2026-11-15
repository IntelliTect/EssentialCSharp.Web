using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace EssentialCSharp.Web.E2ETests;

// Configured via E2E_BASE_URL (required) and E2E_EXPECTED_COMMIT (optional; /version must report it).
public static class DeployedSite
{
    public const string BaseUrlVariable = "E2E_BASE_URL";
    public const string ExpectedCommitVariable = "E2E_EXPECTED_COMMIT";

    private static readonly string? s_rawBaseUrl = Environment.GetEnvironmentVariable(BaseUrlVariable);

    public static Uri? BaseUrl { get; } = ParseBaseUrl(s_rawBaseUrl);

    public static Uri RequiredBaseUrl => BaseUrl ?? throw new InvalidOperationException(
        $"{BaseUrlVariable} must be an absolute http(s) URL, e.g. https://dev.essentialcsharp.com (got '{s_rawBaseUrl}').");

    // Skip only when neither variable is set, so a malformed URL fails loudly instead of the gate passing untested.
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

    // One retry for transient failures (5xx, 408, 429, timeouts) so a blip or shared-IP rate limit does not
    // fail the gate; other statuses, including the 302s and 404s tests assert on, pass through untouched.
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

    // No redirect following (tests assert them); cookies kept for antiforgery-protected form posts.
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

public sealed class RequiresDeployedSiteAttribute()
    : SkipAttribute($"Set {DeployedSite.BaseUrlVariable} to run tests against a deployed site.")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(!DeployedSite.IsRequested);
}
