namespace EssentialCSharp.Web.Services;

public enum CaptchaValidationOutcome
{
    Misconfigured,
    MissingToken,
    Unavailable,
    Invalid,
    Valid
}
