using System.Text.Json;
using EssentialCSharp.Web.Models;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public class CaptchaService : ICaptchaService
{
    private IHttpClientFactory ClientFactory { get; }
    public CaptchaOptions Options { get; } //Set with Secret Manager.

    public CaptchaService(IHttpClientFactory clientFactory, IOptions<CaptchaOptions> optionsAccessor)
    {
        ClientFactory = clientFactory;
        Options = optionsAccessor.Value;
    }

    // Verify captcha. Optionally add overload to pass in remoteIp as in the docs
    // https://docs.hcaptcha.com/#verify-the-user-response-server-side
    public async Task<HCaptchaResult?> Verify(string secret, string response, string sitekey)
    {
        // create post data
        List<KeyValuePair<string, string>> postData = new()
        {
            new KeyValuePair<string, string>("secret", secret),
            new KeyValuePair<string, string>("response", response),
            new KeyValuePair<string, string>("sitekey", sitekey)
        };

        return await PostVerification(postData);
    }

    public async Task<HCaptchaResult?> Verify(string response)
    {
        string secret = Options.SecretKey ?? throw new InvalidOperationException($"{CaptchaOptions.CaptchaSender} {nameof(Options.SecretKey)} is unexpectedly null");
        string sitekey = Options.SiteKey ?? throw new InvalidOperationException($"{CaptchaOptions.CaptchaSender} {nameof(Options.SiteKey)} is unexpectedly null");

        return await Verify(secret, response, sitekey);
    }

    public async Task<HCaptchaResult?> PostVerification(List<KeyValuePair<string, string>> postData)
    {
        HttpClient client = ClientFactory.CreateClient("hCaptcha");

        // request api
        HttpResponseMessage res = await client.PostAsync(
            // base url is given in IHttpClientFactory service registration
            // hCaptcha wants URL-encoded POST
            "/siteverify", new FormUrlEncodedContent(postData));

        // convert JSON string into Class
        return JsonSerializer.Deserialize<HCaptchaResult>(await res.Content.ReadAsStringAsync());
    }
}
