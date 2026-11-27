using System.Net;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests.Identity;

/// <summary>
/// Regression tests for the Register pre-account hijack and for what a password reset clears: a password, external
/// login or MCP token added by someone who never proved control of the email must not survive the owner taking
/// the account over.
/// </summary>
public class RegisterAccountOwnershipTests : IntegrationTestBase
{
    private static async Task<HttpResponseMessage> RegisterAsync(Browser browser, string email, string password) =>
        await browser.PostFormAsync("/Identity/Account/Register", new()
        {
            ["Input.UserName"] = $"user-{Guid.NewGuid():N}",
            ["Input.FirstName"] = "Test",
            ["Input.LastName"] = "User",
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        });

    [Test]
    public async Task SquattersPassword_StopsWorkingWhenTheOwnerConfirmsInTheirOwnBrowser()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string ownerEmail = IdentityFlowHarness.UniqueEmail("owner");
        Browser squatter = harness.NewBrowser();
        Browser owner = harness.NewBrowser();

        using HttpResponseMessage registered = await RegisterAsync(squatter, ownerEmail, IdentityFlowHarness.StrongPassword);
        using HttpResponseMessage confirmed = await owner.GetAsync(
            IdentityFlowHarness.PathAndQuery(harness.LinkFromEmail(ownerEmail, "Confirm your email")));
        string confirmedBody = await confirmed.Content.ReadAsStringAsync();

        EssentialCSharpWebUser? user = await harness.FindUserAsync(ownerEmail);
        using (Assert.Multiple())
        {
            await Assert.That(registered.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(user!.EmailConfirmed).IsTrue();
            await Assert.That(user.PasswordHash).IsNull();
            await Assert.That(confirmedBody).Contains("choose a password");
            await Assert.That(confirmedBody).Contains("id=\"set-password-link\"");
            await Assert.That(await harness.NewBrowser().LogInWithPasswordAsync(ownerEmail, IdentityFlowHarness.StrongPassword)).IsFalse();
        }
    }

    [Test]
    public async Task OwnerCanChooseAPasswordFromTheConfirmationPage()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string ownerEmail = IdentityFlowHarness.UniqueEmail("owner");
        Browser owner = harness.NewBrowser();

        using HttpResponseMessage registered = await RegisterAsync(harness.NewBrowser(), ownerEmail, IdentityFlowHarness.StrongPassword);
        using HttpResponseMessage confirmed = await owner.GetAsync(
            IdentityFlowHarness.PathAndQuery(harness.LinkFromEmail(ownerEmail, "Confirm your email")));
        string body = await confirmed.Content.ReadAsStringAsync();
        string setPasswordUrl = System.Net.WebUtility.HtmlDecode(
            System.Text.RegularExpressions.Regex.Match(body, "id=\"set-password-link\"[^>]*href=\"([^\"]+)\"").Groups[1].Value);

        using HttpResponseMessage reset = await owner.PostFormAsync(
            "/Identity/Account/ResetPassword",
            new()
            {
                ["Input.Email"] = ownerEmail,
                ["Input.Password"] = IdentityFlowHarness.OtherStrongPassword,
                ["Input.ConfirmPassword"] = IdentityFlowHarness.OtherStrongPassword,
                ["Input.Code"] = IdentityFlowHarness.DecodeCode(setPasswordUrl),
            },
            getUrl: setPasswordUrl);

        using (Assert.Multiple())
        {
            await Assert.That(reset.Headers.Location?.OriginalString).Contains("ResetPasswordConfirmation");
            await Assert.That(await harness.NewBrowser().LogInWithPasswordAsync(ownerEmail, IdentityFlowHarness.OtherStrongPassword)).IsTrue();
            await Assert.That(await harness.NewBrowser().LogInWithPasswordAsync(ownerEmail, IdentityFlowHarness.StrongPassword)).IsFalse();
        }
    }

    [Test]
    public async Task Registrant_ConfirmingInTheSameBrowser_KeepsTheirPassword()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string email = IdentityFlowHarness.UniqueEmail("reader");
        Browser reader = harness.NewBrowser();

        using HttpResponseMessage registered = await RegisterAsync(reader, email, IdentityFlowHarness.StrongPassword);
        using HttpResponseMessage confirmed = await reader.GetAsync(
            IdentityFlowHarness.PathAndQuery(harness.LinkFromEmail(email, "Confirm your email")));
        string body = await confirmed.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(body).Contains("Thank you for confirming your email.");
            await Assert.That(body).DoesNotContain("set-password-link");
            await Assert.That(await reader.LogInWithPasswordAsync(email, IdentityFlowHarness.StrongPassword)).IsTrue();
        }
    }

    [Test]
    public async Task PasswordReset_RemovesExternalLoginsRevokesMcpTokensAndEndsSessions()
    {
        IdentityFlowHarness harness = new(Factory.Inner);
        string email = IdentityFlowHarness.UniqueEmail("owner");
        string userId = await harness.CreateConfirmedUserAsync(email, IdentityFlowHarness.StrongPassword);
        (string rawToken, string stampBefore) = await harness.InScopeAsync(async sp =>
        {
            UserManager<EssentialCSharpWebUser> users = sp.GetRequiredService<UserManager<EssentialCSharpWebUser>>();
            EssentialCSharpWebUser user = (await users.FindByIdAsync(userId))!;
            await users.AddLoginAsync(user, new UserLoginInfo("GitHub", "someone-gh-1", "GitHub"));
            (string token, _) = await sp.GetRequiredService<McpApiTokenService>().CreateTokenAsync(userId, "someone");
            return (token, (await users.FindByIdAsync(userId))!.SecurityStamp!);
        });

        await harness.ResetPasswordThroughPagesAsync(harness.NewBrowser(), email, IdentityFlowHarness.OtherStrongPassword);

        (bool tokenStillValid, string stampAfter) = await harness.InScopeAsync(async sp =>
        {
            bool valid = await sp.GetRequiredService<McpApiTokenService>().ResolveValidTokenAsync(rawToken) is not null;
            EssentialCSharpWebUser user = (await sp.GetRequiredService<UserManager<EssentialCSharpWebUser>>().FindByIdAsync(userId))!;
            return (valid, user.SecurityStamp!);
        });
        using (Assert.Multiple())
        {
            await Assert.That(await harness.GetLoginsAsync(email)).IsEmpty();
            await Assert.That(tokenStillValid).IsFalse();
            await Assert.That(stampAfter).IsNotEqualTo(stampBefore);
        }
    }
}
