using System.Text.Json;
using EssentialCSharp.Web.Models;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public partial class CaptchaService(IHttpClientFactory clientFactory, IOptions<CaptchaOptions> optionsAccessor, IOptions<SiteSettings> siteSettingsAccessor, ILogger<CaptchaService> logger) : ICaptchaService
{
    private IHttpClientFactory ClientFactory { get; } = clientFactory;
    private CaptchaOptions Options { get; } = optionsAccessor.Value;
    private SiteSettings SiteSettings { get; } = siteSettingsAccessor.Value;

    // Explicit overload used by integration tests: https://docs.hcaptcha.com/#verify-the-user-response-server-side
    public async Task<HCaptchaResult?> VerifyAsync(string secret, string response, string sitekey, CancellationToken cancellationToken = default)
    {
        List<KeyValuePair<string, string>> postData =
        [
            new("secret", secret),
            new("response", response),
            new("sitekey", sitekey)
        ];

        return await PostVerification(postData, cancellationToken);
    }

    public Task<HCaptchaResult?> VerifyAsync(string? response, CancellationToken cancellationToken = default)
        => VerifyAsync(response, remoteIp: null, cancellationToken);

    public async Task<HCaptchaResult?> VerifyAsync(string? response, string? remoteIp, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }

        string secret = Options.SecretKey ?? throw new InvalidOperationException($"{CaptchaOptions.CaptchaSender} {nameof(Options.SecretKey)} is unexpectedly null");
        string sitekey = Options.SiteKey ?? throw new InvalidOperationException($"{CaptchaOptions.CaptchaSender} {nameof(Options.SiteKey)} is unexpectedly null");

        List<KeyValuePair<string, string>> postData =
        [
            new("secret", secret),
            new("response", response),
            new("sitekey", sitekey)
        ];

        if (Options.VerifyRemoteIp && !string.IsNullOrWhiteSpace(remoteIp))
        {
            postData.Add(new("remoteip", remoteIp));
        }

        HCaptchaResult? result = await PostVerification(postData, cancellationToken);

        if (result is { Success: true })
        {
            string expectedHostname = Uri.TryCreate(SiteSettings.BaseUrl, UriKind.Absolute, out Uri? baseUri)
                ? baseUri.Host
                : SiteSettings.BaseUrl;
            LogHostnameVerified(logger, result.Hostname, expectedHostname);
        }

        return result;
    }

    private async Task<HCaptchaResult?> PostVerification(List<KeyValuePair<string, string>> postData, CancellationToken cancellationToken = default)
    {
        try
        {
            HttpClient client = ClientFactory.CreateClient("hCaptcha");

            // hCaptcha siteverify requires URL-encoded POST; base URL is set in IHttpClientFactory registration
            using FormUrlEncodedContent content = new(postData);
            HttpResponseMessage res = await client.PostAsync("/siteverify", content, cancellationToken);

            res.EnsureSuccessStatusCode();
            return JsonSerializer.Deserialize<HCaptchaResult>(await res.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            LogSiteverifyFailed(logger, ex);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "hCaptcha hostname: reported={ReportedHostname}, expected={ExpectedHostname}")]
    private static partial void LogHostnameVerified(ILogger<CaptchaService> logger, string reportedHostname, string expectedHostname);

    [LoggerMessage(Level = LogLevel.Error, Message = "hCaptcha siteverify request failed")]
    private static partial void LogSiteverifyFailed(ILogger<CaptchaService> logger, Exception exception);
}
