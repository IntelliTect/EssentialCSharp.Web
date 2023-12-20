namespace EssentialCSharp.Web.Services;

internal static class PasswordRequirementOptions
{
    public const int PasswordMinimumLength = 10;
    public const int PasswordMaximumLength = 100;
    public const bool RequireDigit = true;
    public const bool RequireNonAlphanumeric = true;
    public const bool RequireUppercase = true;
    public const bool RequireLowercase = true;
    public const int RequiredUniqueChars = 6;
}
