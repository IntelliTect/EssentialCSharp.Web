namespace EssentialCSharp.Web.Services;

public interface ICaptchaValidationService
{
    Task<CaptchaValidationResult> ValidateAsync(string? response, CancellationToken cancellationToken = default);
    Task<CaptchaValidationResult> ValidateAsync(string? response, string? remoteIp, CancellationToken cancellationToken = default);
}
