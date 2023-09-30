namespace EssentialCSharp.Web.Services
{
    public interface ICaptchaService
    {
        Task<HttpResponseMessage> Verify(string secret, string token, string remoteIp);
        Task<HttpResponseMessage> Verify(string secret, string token);
    }
}
