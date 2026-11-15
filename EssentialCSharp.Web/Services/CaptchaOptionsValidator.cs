using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Fails startup if hCaptcha's public test keys (which accept every request) are configured where they
/// shouldn't be: always in Production, and elsewhere unless <see cref="CaptchaOptions.AllowTestKeys"/> is set.
/// </summary>
public sealed class CaptchaOptionsValidator(IHostEnvironment environment) : IValidateOptions<CaptchaOptions>
{
    // https://docs.hcaptcha.com/#integration-testing-test-keys
    public const string TestSecretKey = "0x0000000000000000000000000000000000000000";
    private static readonly string[] s_testSiteKeys =
    [
        "10000000-ffff-ffff-ffff-000000000001",
        "20000000-ffff-ffff-ffff-000000000002",
        "30000000-ffff-ffff-ffff-000000000003",
    ];

    public ValidateOptionsResult Validate(string? name, CaptchaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        bool usesTestKeys = string.Equals(options.SecretKey, TestSecretKey, StringComparison.Ordinal)
            || s_testSiteKeys.Contains(options.SiteKey, StringComparer.OrdinalIgnoreCase);

        if (!usesTestKeys || environment.IsDevelopment())
        {
            return ValidateOptionsResult.Success;
        }

        if (environment.IsProduction())
        {
            return ValidateOptionsResult.Fail("hCaptcha test keys must never be configured in Production.");
        }

        return options.AllowTestKeys
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"hCaptcha test keys are configured in '{environment.EnvironmentName}'. " +
                $"Set {CaptchaOptions.CaptchaSender}:{nameof(CaptchaOptions.AllowTestKeys)}=true if that is intended.");
    }
}
