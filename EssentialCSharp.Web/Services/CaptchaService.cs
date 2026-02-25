using System.Text.Json;
using EssentialCSharp.Web.Models;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public class CaptchaService(IHttpClientFactory clientFactory, IOptions<CaptchaOptions> optionsAccessor) : ICaptchaService
{
    private IHttpClientFactory ClientFactory { get; } = clientFactory;
    private CaptchaOptions Options { get; } = optionsAccessor.Value;

    // Verify captcha. Optionally add overload to pass in remoteIp as in the docs
    // https://docs.hcaptcha.com/#verify-the-user-response-server-side
    public async Task<HCaptchaResult?> VerifyAsync(string secret, string response, string sitekey, CancellationToken cancellationToken = default)
    {
        // create post data
        List<KeyValuePair<string, string>> postData =
        [
            new KeyValuePair<string, string>("secret", secret),
            new KeyValuePair<string, string>("response", response),
            new KeyValuePair<string, string>("sitekey", sitekey)
        ];

        return await PostVerification(postData, cancellationToken);
    }

    public async Task<HCaptchaResult?> VerifyAsync(string? response, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return null;
        }
        string secret = Options.SecretKey ?? throw new InvalidOperationException($"{CaptchaOptions.CaptchaSender} {nameof(Options.SecretKey)} is unexpectedly null");
        string sitekey = Options.SiteKey ?? throw new InvalidOperationException($"{CaptchaOptions.CaptchaSender} {nameof(Options.SiteKey)} is unexpectedly null");

        return await VerifyAsync(secret, response, sitekey, cancellationToken);
    }

    public async Task<HCaptchaResult?> PostVerification(List<KeyValuePair<string, string>> postData, CancellationToken cancellationToken = default)
    {
        HttpClient client = ClientFactory.CreateClient("hCaptcha");

        // request api
        // hCaptcha wants URL-encoded POST; base url is given in IHttpClientFactory service registration
        using FormUrlEncodedContent content = new(postData);
        HttpResponseMessage res = await client.PostAsync("/siteverify", content, cancellationToken);

        res.EnsureSuccessStatusCode();
        // convert JSON string into Class
        return JsonSerializer.Deserialize<HCaptchaResult>(await res.Content.ReadAsStringAsync(cancellationToken));
    }
}
