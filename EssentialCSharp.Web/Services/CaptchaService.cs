using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public class CaptchaService : ICaptchaService
{
    private IHttpClientFactory ClientFactory { get; }

    public CaptchaService(IHttpClientFactory clientFactory)
    {
        ClientFactory = clientFactory;
    }

    // Verify captcha. Optionally add overload to pass in remoteIp as in the docs
    // https://docs.hcaptcha.com/#verify-the-user-response-server-side
    public async Task<HttpResponseMessage> Verify(string secret, string token, string sitekey)
    {
        // create post data
        List<KeyValuePair<string, string>> postData = new()
        {
            new KeyValuePair<string, string>("secret", secret),
            new KeyValuePair<string, string>("response", token),
            new KeyValuePair<string, string>("sitekey", sitekey)
        };


        return await PostVerification(postData);
    }

    public async Task<HttpResponseMessage> PostVerification(List<KeyValuePair<string, string>> postData)
    {
        HttpClient client = ClientFactory.CreateClient("hCaptcha");

        // request api
        return await client.PostAsync(
            // base url is given in IHttpClientFactory service registration
            // hCaptcha wants URL-encoded POST
            "/siteverify", new FormUrlEncodedContent(postData));
    }
}
