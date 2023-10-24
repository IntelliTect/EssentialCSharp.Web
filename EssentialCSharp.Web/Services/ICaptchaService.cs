using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Services;

public interface ICaptchaService
{
    Task<HCaptchaResult?> Verify(string secret, string response, string sitekey);
    Task<HCaptchaResult?> Verify(string response);
}
