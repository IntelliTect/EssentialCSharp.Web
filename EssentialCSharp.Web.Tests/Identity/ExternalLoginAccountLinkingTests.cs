using System.Net;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests.Identity;

/// <summary>
/// Regression tests for the external-login pre-account hijack: someone who signs in with their own GitHub or
/// Microsoft account must not be able to attach it to an account for an email address they don't control.
/// </summary>
public class ExternalLoginAccountLinkingTests : IntegrationTestBase
{
    private const string Provider = "GitHub";
    private const string CallbackUrl = "/Identity/Account/ExternalLogin?handler=Callback";
    private const string ConfirmationUrl = "/Identity/Account/ExternalLogin?handler=Confirmation";

    private static async Task<HttpResponseMessage> RegisterWithExternalLoginAsync(Browser browser, string email, string captchaResponse = "test-captcha-response") =>
        await browser.PostFormAsync(ConfirmationUrl, new() { ["Input.Email"] = email }, getUrl: CallbackUrl, captchaResponse: captchaResponse);

    private static Browser BrowserSignedInToProvider(IdentityFlowHarness harness, string providerKey)
    {
        Browser browser = harness.NewBrowser();
        browser.SetCookie(harness.CreateExternalLoginCookie(Provider, providerKey));
        return browser;
    }

    [Test]
    public async Task ConfirmationPost_DoesNotLinkTheExternalLoginBeforeTheEmailIsConfirmed()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string victimEmail = IdentityFlowHarness.UniqueEmail("victim");
        Browser attacker = BrowserSignedInToProvider(harness, "attacker-gh-1");

        using HttpResponseMessage response = await RegisterWithExternalLoginAsync(attacker, victimEmail);

        EssentialCSharpWebUser? user = await harness.FindUserAsync(victimEmail);
        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(user).IsNotNull();
            await Assert.That(user!.EmailConfirmed).IsFalse();
            await Assert.That(await harness.GetLoginsAsync(victimEmail)).IsEmpty();
        }
    }

    [Test]
    public async Task PreRegisteredExternalLogin_DoesNotSignInAfterTheOwnerConfirmsAndResetsThePassword()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string victimEmail = IdentityFlowHarness.UniqueEmail("victim");
        Browser attacker = BrowserSignedInToProvider(harness, "attacker-gh-2");
        Browser owner = harness.NewBrowser();

        using HttpResponseMessage registered = await RegisterWithExternalLoginAsync(attacker, victimEmail);
        using HttpResponseMessage confirmed = await owner.GetAsync(
            IdentityFlowHarness.PathAndQuery(harness.LinkFromEmail(victimEmail, "Confirm your email")));
        await harness.ResetPasswordThroughPagesAsync(owner, victimEmail, IdentityFlowHarness.StrongPassword);

        // The attacker replays the provider callback, as they would after the owner adopted the account.
        Browser attackerAgain = BrowserSignedInToProvider(harness, "attacker-gh-2");
        using HttpResponseMessage callback = await attackerAgain.GetAsync(CallbackUrl);

        using (Assert.Multiple())
        {
            await Assert.That(confirmed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(callback.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(attackerAgain.HasCookie(IdentityFlowHarness.ApplicationCookieName)).IsFalse();
            await Assert.That(await harness.GetLoginsAsync(victimEmail)).IsEmpty();
            await Assert.That(await owner.LogInWithPasswordAsync(victimEmail, IdentityFlowHarness.StrongPassword)).IsTrue();
        }
    }

    [Test]
    public async Task ConfirmationLink_OpenedInTheBrowserHoldingTheExternalLogin_LinksItAndAllowsSignIn()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string email = IdentityFlowHarness.UniqueEmail("reader");
        Browser reader = BrowserSignedInToProvider(harness, "reader-gh-1");

        using HttpResponseMessage registered = await RegisterWithExternalLoginAsync(reader, email);
        string confirmLink = harness.LinkFromEmail(email, "Confirm your email");
        using HttpResponseMessage confirmed = await reader.GetAsync(IdentityFlowHarness.PathAndQuery(confirmLink));
        string confirmedBody = await confirmed.Content.ReadAsStringAsync();

        Browser later = BrowserSignedInToProvider(harness, "reader-gh-1");
        using HttpResponseMessage callback = await later.GetAsync(CallbackUrl);

        IList<UserLoginInfo> logins = await harness.GetLoginsAsync(email);
        using (Assert.Multiple())
        {
            await Assert.That(confirmedBody).Contains("account is linked");
            await Assert.That(logins.Select(l => $"{l.LoginProvider}/{l.ProviderKey}")).IsEquivalentTo(["GitHub/reader-gh-1"]);
            await Assert.That(callback.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(later.HasCookie(IdentityFlowHarness.ApplicationCookieName)).IsTrue();
        }
    }

    [Test]
    public async Task ConfirmationLink_OpenedWithADifferentExternalLogin_LinksNeither()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string victimEmail = IdentityFlowHarness.UniqueEmail("victim");
        Browser attacker = BrowserSignedInToProvider(harness, "attacker-gh-3");
        Browser owner = BrowserSignedInToProvider(harness, "owner-gh-3");

        using HttpResponseMessage registered = await RegisterWithExternalLoginAsync(attacker, victimEmail);
        using HttpResponseMessage confirmed = await owner.GetAsync(
            IdentityFlowHarness.PathAndQuery(harness.LinkFromEmail(victimEmail, "Confirm your email")));

        await Assert.That(await harness.GetLoginsAsync(victimEmail)).IsEmpty();
    }

    [Test]
    public async Task ConfirmationLink_OpenedInAnotherBrowser_CanBeFinishedBySigningInAgain()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string email = IdentityFlowHarness.UniqueEmail("reader");
        Browser desktop = BrowserSignedInToProvider(harness, "reader-gh-4");
        Browser phone = harness.NewBrowser();

        using HttpResponseMessage registered = await RegisterWithExternalLoginAsync(desktop, email);
        using HttpResponseMessage confirmedOnPhone = await phone.GetAsync(
            IdentityFlowHarness.PathAndQuery(harness.LinkFromEmail(email, "Confirm your email")));
        string phoneBody = await confirmedOnPhone.Content.ReadAsStringAsync();

        // Signing in with the provider again and re-entering the email sends a fresh link for this browser.
        Browser desktopAgain = BrowserSignedInToProvider(harness, "reader-gh-4");
        using HttpResponseMessage registeredAgain = await RegisterWithExternalLoginAsync(desktopAgain, email);
        using HttpResponseMessage confirmedOnDesktop = await desktopAgain.GetAsync(
            IdentityFlowHarness.PathAndQuery(harness.LinkFromEmail(email, "Confirm your email")));

        using (Assert.Multiple())
        {
            await Assert.That(phoneBody).Contains("log in with GitHub again");
            await Assert.That(harness.Email.CountTo(email)).IsEqualTo(2);
            await Assert.That((await harness.GetLoginsAsync(email)).Select(l => l.ProviderKey)).IsEquivalentTo(["reader-gh-4"]);
        }
    }

    [Test]
    public async Task ConfirmationPost_ForAConfirmedAccountThatCanSignIn_SendsNoLinkEmail()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string email = IdentityFlowHarness.UniqueEmail("member");
        await harness.CreateConfirmedUserAsync(email, IdentityFlowHarness.StrongPassword);
        Browser attacker = BrowserSignedInToProvider(harness, "attacker-gh-5");

        using HttpResponseMessage response = await RegisterWithExternalLoginAsync(attacker, email);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(harness.Email.CountTo(email)).IsEqualTo(0);
            await Assert.That(await harness.GetLoginsAsync(email)).IsEmpty();
        }
    }

    [Test]
    [Arguments("")]
    [Arguments("invalid-captcha-test-token")]
    public async Task ConfirmationPost_WithoutAValidCaptcha_CreatesNoAccountAndSendsNoEmail(string captchaResponse)
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string email = IdentityFlowHarness.UniqueEmail("anyone");
        Browser browser = BrowserSignedInToProvider(harness, "bot-gh-1");

        using HttpResponseMessage response = await RegisterWithExternalLoginAsync(browser, email, captchaResponse);
        string body = await response.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body).Contains("Human verification failed");
            await Assert.That(await harness.FindUserAsync(email)).IsNull();
            await Assert.That(harness.Email.CountTo(email)).IsEqualTo(0);
        }
    }

    [Test]
    public async Task FirstConfirmation_RemovesExternalLoginsLinkedWhileTheEmailWasUnverified()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string victimEmail = IdentityFlowHarness.UniqueEmail("victim");
        // An account pre-linked before this fix shipped: unconfirmed, with someone's provider login on it.
        string confirmLink = await harness.InScopeAsync(async sp =>
        {
            UserManager<EssentialCSharpWebUser> users = sp.GetRequiredService<UserManager<EssentialCSharpWebUser>>();
            EssentialCSharpWebUser user = new() { UserName = victimEmail, Email = victimEmail };
            await users.CreateAsync(user);
            await users.AddLoginAsync(user, new UserLoginInfo(Provider, "attacker-gh-6", Provider));
            string code = await users.GenerateEmailConfirmationTokenAsync(user);
            return $"/Identity/Account/ConfirmEmail?userId={user.Id}&code={Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(System.Text.Encoding.UTF8.GetBytes(code))}";
        });

        using HttpResponseMessage confirmed = await harness.NewBrowser().GetAsync(confirmLink);
        Browser attacker = BrowserSignedInToProvider(harness, "attacker-gh-6");
        using HttpResponseMessage callback = await attacker.GetAsync(CallbackUrl);

        using (Assert.Multiple())
        {
            await Assert.That(confirmed.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await harness.GetLoginsAsync(victimEmail)).IsEmpty();
            await Assert.That(attacker.HasCookie(IdentityFlowHarness.ApplicationCookieName)).IsFalse();
        }
    }
}
