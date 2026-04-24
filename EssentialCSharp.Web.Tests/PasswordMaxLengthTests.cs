using System.ComponentModel.DataAnnotations;
using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Tests that max-length validation blocks oversized passwords on verification endpoints
/// before they can reach PBKDF2 hashing (long-password DoS prevention).
/// </summary>
public class PasswordMaxLengthTests
{
    private static string OverlongPassword => new string('a', PasswordRequirementOptions.PasswordMaximumLength + 1);
    private static string MaxLengthPassword => new string('a', PasswordRequirementOptions.PasswordMaximumLength);

    // ── Login ────────────────────────────────────────────────────────────────

    [Test]
    public async Task Login_PasswordAtMaxLength_PassesValidation()
    {
        var model = new Areas.Identity.Pages.Account.LoginModel.InputModel
        {
            Email = "user@example.com",
            Password = MaxLengthPassword,
        };
        IList<ValidationResult> results = Validate(model);
        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task Login_PasswordExceedingMaxLength_FailsValidation()
    {
        var model = new Areas.Identity.Pages.Account.LoginModel.InputModel
        {
            Email = "user@example.com",
            Password = OverlongPassword,
        };
        IList<ValidationResult> results = Validate(model);
        await Assert.That(results).IsNotEmpty();
        await Assert.That(results.Any(r => r.MemberNames.Contains(nameof(model.Password)))).IsTrue();
    }

    // ── ChangePassword (OldPassword) ─────────────────────────────────────────

    [Test]
    public async Task ChangePassword_OldPasswordAtMaxLength_PassesValidation()
    {
        var model = new Areas.Identity.Pages.Account.Manage.ChangePasswordModel.InputModel
        {
            OldPassword = MaxLengthPassword,
            NewPassword = "ValidNewPassphrase15",
            ConfirmPassword = "ValidNewPassphrase15",
        };
        IList<ValidationResult> results = Validate(model);
        await Assert.That(results.Any(r => r.MemberNames.Contains(nameof(model.OldPassword)))).IsFalse();
    }

    [Test]
    public async Task ChangePassword_OldPasswordExceedingMaxLength_FailsValidation()
    {
        var model = new Areas.Identity.Pages.Account.Manage.ChangePasswordModel.InputModel
        {
            OldPassword = OverlongPassword,
            NewPassword = "ValidNewPassphrase15",
            ConfirmPassword = "ValidNewPassphrase15",
        };
        IList<ValidationResult> results = Validate(model);
        await Assert.That(results.Any(r => r.MemberNames.Contains(nameof(model.OldPassword)))).IsTrue();
    }

    // ── DeletePersonalData ───────────────────────────────────────────────────

    [Test]
    public async Task DeletePersonalData_PasswordAtMaxLength_PassesValidation()
    {
        var model = new Areas.Identity.Pages.Account.Manage.DeletePersonalDataModel.InputModel
        {
            Password = MaxLengthPassword,
        };
        IList<ValidationResult> results = Validate(model);
        await Assert.That(results).IsEmpty();
    }

    [Test]
    public async Task DeletePersonalData_PasswordExceedingMaxLength_FailsValidation()
    {
        var model = new Areas.Identity.Pages.Account.Manage.DeletePersonalDataModel.InputModel
        {
            Password = OverlongPassword,
        };
        IList<ValidationResult> results = Validate(model);
        await Assert.That(results).IsNotEmpty();
        await Assert.That(results.Any(r => r.MemberNames.Contains(nameof(model.Password)))).IsTrue();
    }

    // ── Policy constants sanity checks ───────────────────────────────────────

    [Test]
    public async Task PasswordMinimumLength_IsAtLeast15_PerNistGuidance()
    {
        int minLength = PasswordRequirementOptions.PasswordMinimumLength;
        await Assert.That(minLength).IsGreaterThanOrEqualTo(15);
    }

    [Test]
    public async Task PasswordMaximumLength_IsAtLeast64_PerOwaspGuidance()
    {
        int maxLength = PasswordRequirementOptions.PasswordMaximumLength;
        await Assert.That(maxLength).IsGreaterThanOrEqualTo(64);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static List<ValidationResult> Validate(object model)
    {
        var ctx = new ValidationContext(model);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, ctx, results, validateAllProperties: true);
        return results;
    }
}
