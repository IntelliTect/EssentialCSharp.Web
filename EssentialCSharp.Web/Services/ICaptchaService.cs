namespace EssentialCSharp.Web.Services;

public interface ICaptchaService
{
    Task<HttpResponseMessage> Verify(string secret, string token, string sitekey);
}
