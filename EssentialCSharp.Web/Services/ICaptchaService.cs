using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Services;

public interface ICaptchaService
{
    Task<HCaptchaResult?> VerifyAsync(string? response, CancellationToken cancellationToken = default);
    Task<HCaptchaResult?> VerifyAsync(string? response, string? remoteIp, CancellationToken cancellationToken = default);
}
