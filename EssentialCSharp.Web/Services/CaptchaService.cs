using System.Text.Json;
using EssentialCSharp.Web.Models;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public class CaptchaService(IHttpClientFactory clientFactory, IOptions<CaptchaOptions> optionsAccessor, ILogger<CaptchaService> logger) : ICaptchaService
{
    private IHttpClientFactory ClientFactory { get; } = clientFactory;
    private CaptchaOptions Options { get; } = optionsAccessor.Value;

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

        if (result is { Success: true } && Options.ExpectedHostname is { Length: > 0 } expectedHostname)
        {
            if (!string.Equals(result.Hostname, expectedHostname, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning("hCaptcha hostname mismatch: expected {Expected}, got {Actual}", expectedHostname, result.Hostname);
                result.Success = false;
            }
        }

        return result;
    }

    private async Task<HCaptchaResult?> PostVerification(List<KeyValuePair<string, string>> postData, CancellationToken cancellationToken = default)
    {
        HttpClient client = ClientFactory.CreateClient("hCaptcha");

        // hCaptcha siteverify requires URL-encoded POST; base URL is set in IHttpClientFactory registration
        using FormUrlEncodedContent content = new(postData);
        HttpResponseMessage res = await client.PostAsync("/siteverify", content, cancellationToken);

        res.EnsureSuccessStatusCode();
        return JsonSerializer.Deserialize<HCaptchaResult>(await res.Content.ReadAsStringAsync(cancellationToken));
    }
}
