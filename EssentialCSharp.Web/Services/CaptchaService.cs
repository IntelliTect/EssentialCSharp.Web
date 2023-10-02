using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services
{
    public class CaptchaService : ICaptchaService
    {
        private IHttpClientFactory ClientFactory { get; }

        public CaptchaService(IHttpClientFactory clientFactory)
        {
            ClientFactory = clientFactory;
        }

        public async Task<HttpResponseMessage> Verify(string secret, string token)
        {
            // create post data
            List<KeyValuePair<string, string>> postData = new()
            {
                new KeyValuePair<string, string>("secret", secret),
                new KeyValuePair<string, string>("response", token),
            };


            return await PostVerification(postData);
        }

        public async Task<HttpResponseMessage> Verify(string secret, string token, string remoteIp)
        {
            // create post data
            List<KeyValuePair<string, string>> postData = new()
            {
                new KeyValuePair<string, string>("secret", secret),
                new KeyValuePair<string, string>("response", token),
                new KeyValuePair<string, string>("remoteip", remoteIp)
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
}
