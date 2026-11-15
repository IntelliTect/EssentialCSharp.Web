namespace EssentialCSharp.Web.Services;

public class CaptchaOptions
{
    public const string CaptchaSender = "HCaptcha";
    public required string SecretKey { get; set; }
    public required string SiteKey { get; set; }

    /// <summary>
    /// Allows hCaptcha's public test keys outside Development, e.g. on a dev deployment that automated
    /// tests log in to. Never honoured in Production.
    /// </summary>
    public bool AllowTestKeys { get; set; }

    /// <summary>
    /// The hCaptcha base URL
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://api.hcaptcha.com/";

    /// <summary>
    /// The HTTP Post Form Key to get the token from
    /// </summary>
    public const string HttpPostResponseKeyName = "h-captcha-response";

    /// <summary>
    /// If true, the client IP is passed to hCaptcha token verification.
    /// </summary>
    public bool VerifyRemoteIp { get; set; } = true;

    /// <summary>
    /// Full URL to hCaptcha JavaScript. Leave null to use the layout's global script tag.
    /// </summary>
    public string JavaScriptUrl { get; set; } = "https://js.hcaptcha.com/1/api.js";

}
