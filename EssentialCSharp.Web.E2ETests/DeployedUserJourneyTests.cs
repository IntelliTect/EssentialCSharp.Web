using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace EssentialCSharp.Web.E2ETests;

// Logs in as a real user through the real hCaptcha widget (dev uses hCaptcha's test keys), so it covers what the
// PR journeys fake: the deployed login, the hCaptcha script and CSP, and the configured chat backend.
[Category("Deployed")]
[RequiresDeployedSite]
// No trace: it records typed values, including the test user's password, and the artifacts are public.
[CaptureOnFailure(Trace = false)]
public class DeployedUserJourneyTests : PageTest
{
    public const string EmailVariable = "E2E_USER_EMAIL";
    public const string PasswordVariable = "E2E_USER_PASSWORD";

    // Otherwise the consent banner covers the chat button.
    private const string ConsentCookieValue = "%7B%22analytics_storage%22%3A%22granted%22%2C%22_version%22%3A%223%22%7D";

    [Before(Test)]
    public async Task AcceptCookieConsent()
    {
        await Context.AddCookiesAsync(
        [
            new Cookie { Name = "essential-csharp-consent", Value = ConsentCookieValue, Url = DeployedSite.RequiredBaseUrl.GetLeftPart(UriPartial.Authority) },
        ]);
    }

    [Test]
    public async Task Reader_CanLogInAndLogOut()
    {
        await LogInAsync();
        await Page.Locator("#logout").ClickAsync();

        await Expect(Page.Locator("#login")).ToBeVisibleAsync();
    }

    [Test]
    public async Task LoggedInReader_AskingAQuestion_GetsAReply()
    {
        await LogInAsync();
        await GoToAsync("home");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Open AI Chat Assistant" }).ClickAsync();
        await Page.Locator("#chat-input").FillAsync("In one sentence, what is a C# class?");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send message" }).ClickAsync();

        // The model's wording varies, so only require a finished, non-error reply.
        await Expect(Page.Locator(".typing-indicator")).ToHaveCountAsync(0, new() { Timeout = 90_000 });
        await Expect(Page.Locator(".message-text h4", new() { HasText = "Error" })).ToHaveCountAsync(0);
        await Expect(Page.Locator(".message-bubble.assistant .message-content")).ToContainTextAsync("class", new() { IgnoreCase = true });
    }

    private Task<IResponse?> GoToAsync(string path) => Page.GotoAsync(new Uri(DeployedSite.RequiredBaseUrl, path).ToString());

    private async Task LogInAsync()
    {
        string email = RequiredVariable(EmailVariable);
        string password = RequiredVariable(PasswordVariable);

        await GoToAsync("Identity/Account/Login");
        await Page.Locator("#Input_Email").FillAsync(email);
        await Page.Locator("#Input_Password").FillAsync(password);
        await Page.Locator("#login-submit").ClickAsync();

        // Wait for any outcome so a rejected login fails fast with the reason. Each wrong password counts toward
        // lockout, so a stale E2E_USER_PASSWORD locks the account after a few runs unless lockout is off for it.
        ILocator loggedIn = Page.Locator("#logout");
        ILocator loginErrors = Page.Locator(".validation-summary-errors");
        ILocator lockedOut = Page.GetByRole(AriaRole.Heading, new() { Name = "Locked out" });
        await Expect(loggedIn.Or(loginErrors).Or(lockedOut)).ToBeVisibleAsync(new() { Timeout = 30_000 });
        if (await lockedOut.IsVisibleAsync())
        {
            throw new InvalidOperationException($"The test user is locked out. Check {PasswordVariable}, and disable lockout for this user.");
        }
        if (await loginErrors.IsVisibleAsync())
        {
            throw new InvalidOperationException($"Login was rejected: {await loginErrors.InnerTextAsync()}");
        }
    }

    // Fails rather than skips: a gate that silently stops logging in would pass while login is broken.
    private static string RequiredVariable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} must be set to the dev test user's credentials.");
}
