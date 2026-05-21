using EssentialCSharp.Web.Models;

namespace EssentialCSharp.Web.Services;

public sealed record CaptchaValidationResult(CaptchaValidationOutcome Outcome, HCaptchaResult? Response)
{
    public bool ShouldProceed => Outcome is CaptchaValidationOutcome.Valid;
}
