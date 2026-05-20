using EssentialCSharp.Web.Models;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public sealed class CaptchaValidationService(ICaptchaService captchaService, IOptions<CaptchaOptions> optionsAccessor) : ICaptchaValidationService
{
    private CaptchaOptions Options { get; } = optionsAccessor.Value;

    public Task<CaptchaValidationResult> ValidateAsync(string? response, CancellationToken cancellationToken = default)
        => ValidateAsync(response, remoteIp: null, cancellationToken);

    /// <summary>
    /// Validates a captcha response.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: Both <see cref="CaptchaOptions.SecretKey"/> and <see cref="CaptchaOptions.SiteKey"/> must be configured
    /// (non-empty) for captcha validation to be enabled. If either key is missing, the validation is marked as
    /// <see cref="CaptchaValidationOutcome.Disabled"/> and the captcha service is not invoked. This is intentional:
    /// HCaptcha requires both keys to function properly, and partial configuration (one key set, one missing)
    /// indicates a deployment configuration error that should be detected early.
    /// </remarks>
    public async Task<CaptchaValidationResult> ValidateAsync(string? response, string? remoteIp, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Options.SecretKey) || string.IsNullOrWhiteSpace(Options.SiteKey))
            return new CaptchaValidationResult(CaptchaValidationOutcome.Disabled, null);

        if (string.IsNullOrWhiteSpace(response))
            return new CaptchaValidationResult(CaptchaValidationOutcome.MissingToken, null);

        HCaptchaResult? result = await captchaService.VerifyAsync(response, remoteIp, cancellationToken);
        if (result is null)
            return new CaptchaValidationResult(CaptchaValidationOutcome.Unavailable, null);

        return new CaptchaValidationResult(
            result.Success ? CaptchaValidationOutcome.Valid : CaptchaValidationOutcome.Invalid,
            result);
    }
}
