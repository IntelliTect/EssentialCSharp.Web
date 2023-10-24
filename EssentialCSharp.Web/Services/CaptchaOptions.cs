namespace EssentialCSharp.Web.Services
{
    public class CaptchaOptions
    {
        public const string CaptchaSender = "HCaptcha";
        public string? SiteKey { get; set; }
        public string? SecretKey { get; set; }

        /// <summary>
        /// The hCaptcha base URL
        /// </summary>
        public string ApiBaseUrl { get; set; } = "https://hcaptcha.com/";

        /// <summary>
        /// The HTTP Post Form Key to get the token from
        /// </summary>
        public const string HttpPostResponseKeyName = "h-captcha-response";

        /// <summary>
        /// if true client IP is passed to hCaptcha token verification
        /// </summary>
        public bool VerifyRemoteIp { get; set; } = true;

        /// <summary>
        ///  Full Url to hCaptchy JavaScript
        /// </summary>
        public string JavaScriptUrl { get; set; } = "https://hcaptcha.com/1/api.js";
    }
}
