using EssentialCSharp.Web.Models;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

public sealed partial class CaptchaValidationService(
    ICaptchaService captchaService,
    IOptions<CaptchaOptions> optionsAccessor,
    ILogger<CaptchaValidationService> logger) : ICaptchaValidationService
{
    private CaptchaOptions Options { get; } = optionsAccessor.Value;
    private bool IsCaptchaConfigurationComplete =>
        !string.IsNullOrWhiteSpace(Options.SecretKey) &&
        !string.IsNullOrWhiteSpace(Options.SiteKey);
    private int _configurationWarningLogged;

    public Task<CaptchaValidationResult> ValidateAsync(string? response, CancellationToken cancellationToken = default)
        => ValidateAsync(response, remoteIp: null, cancellationToken);

    /// <summary>
    /// Validates a captcha response.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: Both <see cref="CaptchaOptions.SecretKey"/> and <see cref="CaptchaOptions.SiteKey"/> must be configured
    /// (non-empty) for captcha validation to be enabled. If either key is missing, the validation is marked as
    /// <see cref="CaptchaValidationOutcome.Misconfigured"/> and the captcha service is not invoked. This is intentional:
    /// HCaptcha requires both keys to function properly, and partial configuration (one key set, one missing)
    /// indicates a deployment configuration error that should be detected early.
    /// </remarks>
    public async Task<CaptchaValidationResult> ValidateAsync(string? response, string? remoteIp, CancellationToken cancellationToken = default)
    {
        if (!IsCaptchaConfigurationComplete)
        {
            if (Interlocked.Exchange(ref _configurationWarningLogged, 1) == 0)
                LogConfigurationMissing(logger);

            return CaptchaValidationResult.Misconfigured();
        }

        if (string.IsNullOrWhiteSpace(response))
            return CaptchaValidationResult.MissingToken();

        HCaptchaResult? result = await captchaService.VerifyAsync(response, remoteIp, cancellationToken);
        if (result is null)
            return CaptchaValidationResult.Unavailable();

        return CaptchaValidationResult.FromResponse(result);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "hCaptcha configuration is incomplete; validation is failing closed")]
    private static partial void LogConfigurationMissing(ILogger<CaptchaValidationService> logger);
}
