using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Services;

/// <summary>
/// Fails startup when the Mailjet credentials or the From address are missing or malformed, so a bad
/// deployment is caught at boot instead of at the first registration or password-reset email.
/// <see cref="AuthMessageSenderOptions.SendFromName"/> is optional (Mailjet only requires <c>From.Email</c>).
/// </summary>
public sealed class AuthMessageSenderOptionsValidator : IValidateOptions<AuthMessageSenderOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthMessageSenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];
        string section = AuthMessageSenderOptions.AuthMessageSender;

        if (string.IsNullOrWhiteSpace(options.APIKey))
        {
            failures.Add($"{section}:{nameof(AuthMessageSenderOptions.APIKey)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            failures.Add($"{section}:{nameof(AuthMessageSenderOptions.SecretKey)} is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SendFromEmail))
        {
            failures.Add($"{section}:{nameof(AuthMessageSenderOptions.SendFromEmail)} is required.");
        }
        else if (!IsBareEmailAddress(options.SendFromEmail))
        {
            failures.Add($"{section}:{nameof(AuthMessageSenderOptions.SendFromEmail)} must be a valid email address (for example no-reply@example.com).");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    // MailAddress also accepts display-name forms such as "Name <a@b.com>"; the address must be bare
    // because the display name is configured separately via SendFromName. It also accepts single-label
    // hosts such as "a@localhost", which Mailjet rejects at send time, so require a dotted domain.
    private static bool IsBareEmailAddress(string value) =>
        MailAddress.TryCreate(value, out MailAddress? address)
        && string.Equals(address.Address, value, StringComparison.Ordinal)
        && address.Host.Contains('.')
        && !address.Host.EndsWith('.');
}
