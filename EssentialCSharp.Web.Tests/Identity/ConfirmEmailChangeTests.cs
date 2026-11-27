using System.Net;
using EssentialCSharp.Web.Areas.Identity.Data;

namespace EssentialCSharp.Web.Tests.Identity;

/// <summary>
/// Regression tests for the email-change pre-account hijack: opening the change link must not attach the new
/// address to the requesting account unless the requester (not just the address owner) is the one confirming.
/// </summary>
public class ConfirmEmailChangeTests : IntegrationTestBase
{
    private static async Task<(IdentityFlowHarness Harness, Browser Requester, string RequesterEmail, string NewEmail, string Link)> RequestChangeAsync(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> factory)
    {
        IdentityFlowHarness harness = new(factory);
        string requesterEmail = IdentityFlowHarness.UniqueEmail("requester");
        string newEmail = IdentityFlowHarness.UniqueEmail("owner");
        await harness.CreateConfirmedUserAsync(requesterEmail, IdentityFlowHarness.StrongPassword);
        Browser requester = harness.NewBrowser();
        if (!await requester.LogInWithPasswordAsync(requesterEmail, IdentityFlowHarness.StrongPassword))
        {
            throw new InvalidOperationException("The requester could not log in.");
        }

        using HttpResponseMessage change = await requester.PostFormAsync(
            "/Identity/Account/Manage/Email?handler=ChangeEmail",
            new() { ["Input.NewEmail"] = newEmail },
            getUrl: "/Identity/Account/Manage/Email");
        string link = IdentityFlowHarness.PathAndQuery(harness.LinkFromEmail(newEmail, "Confirm your email change"));
        return (harness, requester, requesterEmail, newEmail, link);
    }

    [Test]
    public async Task ChangeEmailMessage_DescribesTheChangeRatherThanARegistration()
    {
        (IdentityFlowHarness harness, _, _, string newEmail, _) = await RequestChangeAsync(Factory.Inner);

        string body = harness.Email.Last(newEmail, "Confirm your email change").Body;
        await Assert.That(body).Contains("change the email address");
    }

    [Test]
    public async Task AnonymousClickOnTheChangeLink_DoesNotChangeTheEmail()
    {
        (IdentityFlowHarness harness, _, string requesterEmail, string newEmail, string link) = await RequestChangeAsync(Factory.Inner);

        using HttpResponseMessage click = await harness.NewBrowser().GetAsync(link);

        using (Assert.Multiple())
        {
            await Assert.That(click.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
            await Assert.That(click.Headers.Location?.OriginalString).Contains("/Identity/Account/Login");
            await Assert.That(await harness.FindUserAsync(newEmail)).IsNull();
            await Assert.That(await harness.FindUserAsync(requesterEmail)).IsNotNull();
        }
    }

    [Test]
    public async Task ClickByADifferentSignedInUser_DoesNotChangeTheEmail()
    {
        (IdentityFlowHarness harness, _, string requesterEmail, string newEmail, string link) = await RequestChangeAsync(Factory.Inner);
        string otherEmail = IdentityFlowHarness.UniqueEmail("other");
        await harness.CreateConfirmedUserAsync(otherEmail, IdentityFlowHarness.StrongPassword);
        Browser other = harness.NewBrowser();
        await other.LogInWithPasswordAsync(otherEmail, IdentityFlowHarness.StrongPassword);

        using HttpResponseMessage click = await other.GetAsync(link);
        string body = await click.Content.ReadAsStringAsync();

        using (Assert.Multiple())
        {
            await Assert.That(click.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(body).Contains("Error changing email");
            await Assert.That(await harness.FindUserAsync(newEmail)).IsNull();
            await Assert.That(await harness.FindUserAsync(requesterEmail)).IsNotNull();
        }
    }

    [Test]
    public async Task ClickByTheSignedInRequester_ChangesTheEmail()
    {
        (IdentityFlowHarness harness, Browser requester, string requesterEmail, string newEmail, string link) = await RequestChangeAsync(Factory.Inner);

        using HttpResponseMessage click = await requester.GetAsync(link);
        string body = await click.Content.ReadAsStringAsync();

        EssentialCSharpWebUser? changed = await harness.FindUserAsync(newEmail);
        using (Assert.Multiple())
        {
            await Assert.That(body).Contains("Thank you for confirming your email change.");
            await Assert.That(changed).IsNotNull();
            await Assert.That(await harness.FindUserAsync(requesterEmail)).IsNull();
        }
    }
}
