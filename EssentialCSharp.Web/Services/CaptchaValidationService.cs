using EssentialCSharp.Web.Models;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public sealed class CaptchaValidationService(ICaptchaService captchaService, IOptions<CaptchaOptions> optionsAccessor) : ICaptchaValidationService
{
    private CaptchaOptions Options { get; } = optionsAccessor.Value;

    public Task<CaptchaValidationResult> ValidateAsync(string? response, CancellationToken cancellationToken = default)
        => ValidateAsync(response, remoteIp: null, cancellationToken);

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
