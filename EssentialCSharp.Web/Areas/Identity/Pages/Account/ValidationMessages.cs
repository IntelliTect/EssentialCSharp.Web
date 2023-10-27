namespace EssentialCSharp.Web.Areas.Identity.Pages.Account;

public static class ValidationMessages
{
    public const string StringLengthErrorMessage = "The {0} must be at least {2} and at max {1} characters long.";
    public const int PasswordMinimumLength = 10;
    public const int PasswordMaximumLength = 100;
    public const int VerificationCodeMaximumLength = 7;
    public const int VerificationCodeMinimumLength = 6;
}
