using System.Net;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests.Identity;

/// <summary>
/// Regression tests for the smaller account findings: the RegisterConfirmation existence oracle, email links that
/// took their origin from the Host header, and lockout of accounts that have no password.
/// </summary>
public class AccountRecoveryHardeningTests : IntegrationTestBase
{
    [Test]
    public async Task RegisterConfirmation_LooksTheSameForRegisteredAndUnknownEmails()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string registered = IdentityFlowHarness.UniqueEmail("registered");
        await harness.CreateConfirmedUserAsync(registered, IdentityFlowHarness.StrongPassword);
        string unknown = IdentityFlowHarness.UniqueEmail("unknown");

        using HttpResponseMessage forRegistered = await harness.NewBrowser().GetAsync($"/Identity/Account/RegisterConfirmation?email={Uri.EscapeDataString(registered)}");
        using HttpResponseMessage forUnknown = await harness.NewBrowser().GetAsync($"/Identity/Account/RegisterConfirmation?email={Uri.EscapeDataString(unknown)}");
        string registeredBody = await forRegistered.Content.ReadAsStringAsync();
        string unknownBody = await forUnknown.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(forRegistered.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(forUnknown.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(registeredBody).Contains("Please check your email to confirm your account");
            await Assert.That(unknownBody).Contains("Please check your email to confirm your account");
            await Assert.That(unknownBody).DoesNotContain("Unable to load user");
        }
    }

    [Test]
    public async Task PasswordResetLink_UsesTheConfiguredSiteUrlEvenWithAForgedHostHeader()
    {
        IdentityFlowHarness harness = new(Factory.Inner, hostHeader: "attacker.example");
        string email = IdentityFlowHarness.UniqueEmail("victim");
        await harness.CreateConfirmedUserAsync(email, IdentityFlowHarness.StrongPassword);
        string siteUrl = harness.Services.GetRequiredService<IOptions<SiteSettings>>().Value.BaseUrl.TrimEnd('/');

        using HttpResponseMessage forgot = await harness.NewBrowser().PostFormAsync("/Identity/Account/ForgotPassword", new() { ["Input.Email"] = email });
        string link = harness.LinkFromEmail(email, "Reset Password");

        using (Assert.Multiple())
        {
            await Assert.That(link).StartsWith($"{siteUrl}/Identity/Account/ResetPassword?", StringComparison.Ordinal);
            await Assert.That(link).DoesNotContain("attacker.example");
        }
    }

    [Test]
    public async Task ConfirmationLink_UsesTheConfiguredSiteUrlEvenWithAForgedHostHeader()
    {
        IdentityFlowHarness harness = new(Factory.Inner, hostHeader: "attacker.example");
        string email = IdentityFlowHarness.UniqueEmail("reader");
        string siteUrl = harness.Services.GetRequiredService<IOptions<SiteSettings>>().Value.BaseUrl.TrimEnd('/');

        using HttpResponseMessage registered = await harness.NewBrowser().PostFormAsync("/Identity/Account/Register", new()
        {
            ["Input.UserName"] = $"user-{Guid.NewGuid():N}",
            ["Input.FirstName"] = "Test",
            ["Input.LastName"] = "User",
            ["Input.Email"] = email,
            ["Input.Password"] = IdentityFlowHarness.StrongPassword,
            ["Input.ConfirmPassword"] = IdentityFlowHarness.StrongPassword,
        });
        string link = harness.LinkFromEmail(email, "Confirm your email");

        await Assert.That(link).StartsWith($"{siteUrl}/Identity/Account/ConfirmEmail?", StringComparison.Ordinal);
    }

    [Test]
    public async Task WrongPasswords_DoNotLockAnAccountThatHasNoPassword()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string email = IdentityFlowHarness.UniqueEmail("github-only");
        string userId = await harness.CreateConfirmedUserAsync(email, password: null);
        await harness.InScopeAsync(async sp =>
        {
            UserManager<EssentialCSharpWebUser> users = sp.GetRequiredService<UserManager<EssentialCSharpWebUser>>();
            return await users.AddLoginAsync((await users.FindByIdAsync(userId))!, new UserLoginInfo("GitHub", "github-only-1", "GitHub"));
        });

        for (int attempt = 0; attempt < 5; attempt++)
        {
            await harness.NewBrowser().LogInWithPasswordAsync(email, IdentityFlowHarness.StrongPassword);
        }

        Browser owner = harness.NewBrowser();
        owner.SetCookie(harness.CreateExternalLoginCookie("GitHub", "github-only-1"));
        using HttpResponseMessage callback = await owner.GetAsync("/Identity/Account/ExternalLogin?handler=Callback");

        EssentialCSharpWebUser user = (await harness.FindUserAsync(email))!;
        using (Assert.Multiple())
        {
            await Assert.That(user.AccessFailedCount).IsEqualTo(0);
            await Assert.That(user.LockoutEnd).IsNull();
            await Assert.That(callback.Headers.Location?.OriginalString).IsEqualTo("/");
            await Assert.That(owner.HasCookie(IdentityFlowHarness.ApplicationCookieName)).IsTrue();
        }
    }

    [Test]
    public async Task WrongPasswords_StillLockAnAccountThatHasAPassword()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string email = IdentityFlowHarness.UniqueEmail("member");
        await harness.CreateConfirmedUserAsync(email, IdentityFlowHarness.StrongPassword);

        for (int attempt = 0; attempt < 3; attempt++)
        {
            await harness.NewBrowser().LogInWithPasswordAsync(email, IdentityFlowHarness.OtherStrongPassword);
        }

        EssentialCSharpWebUser user = (await harness.FindUserAsync(email))!;
        await Assert.That(user.LockoutEnd.GetValueOrDefault()).IsGreaterThan(DateTimeOffset.UtcNow);
    }

    [Test]
    public async Task PasswordReset_ClearsALockoutCausedByOtherPeoplesGuesses()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string email = IdentityFlowHarness.UniqueEmail("member");
        await harness.CreateConfirmedUserAsync(email, IdentityFlowHarness.StrongPassword);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await harness.NewBrowser().LogInWithPasswordAsync(email, IdentityFlowHarness.OtherStrongPassword);
        }

        await harness.ResetPasswordThroughPagesAsync(harness.NewBrowser(), email, IdentityFlowHarness.OtherStrongPassword);

        using (Assert.Multiple())
        {
            await Assert.That((await harness.FindUserAsync(email))!.LockoutEnd).IsNull();
            await Assert.That(await harness.NewBrowser().LogInWithPasswordAsync(email, IdentityFlowHarness.OtherStrongPassword)).IsTrue();
        }
    }
}
