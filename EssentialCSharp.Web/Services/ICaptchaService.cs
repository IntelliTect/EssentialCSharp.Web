using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Services;

public interface ICaptchaService
{
    Task<HCaptchaResult?> VerifyAsync(string secret, string response, string sitekey);
    Task<HCaptchaResult?> VerifyAsync(string? response);
}
