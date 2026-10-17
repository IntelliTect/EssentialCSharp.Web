using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Services;

public sealed class CaptchaValidationResult
{
    private CaptchaValidationResult(CaptchaValidationOutcome outcome, HCaptchaResult? response)
    {
        Outcome = outcome;
        Response = response;
    }

    public CaptchaValidationOutcome Outcome { get; }

    public HCaptchaResult? Response { get; }

    public bool ShouldProceed => Outcome is CaptchaValidationOutcome.Valid;

    public static CaptchaValidationResult Misconfigured() => new(CaptchaValidationOutcome.Misconfigured, null);

    public static CaptchaValidationResult MissingToken() => new(CaptchaValidationOutcome.MissingToken, null);

    public static CaptchaValidationResult Unavailable() => new(CaptchaValidationOutcome.Unavailable, null);

    public static CaptchaValidationResult FromResponse(HCaptchaResult response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return new(
            response.Success ? CaptchaValidationOutcome.Valid : CaptchaValidationOutcome.Invalid,
            response);
    }
}
