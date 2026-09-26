namespace EssentialCSharp.Web.Services;

public enum CaptchaValidationOutcome
{
    Disabled,
    MissingToken,
    Unavailable,
    Invalid,
    Valid
}
