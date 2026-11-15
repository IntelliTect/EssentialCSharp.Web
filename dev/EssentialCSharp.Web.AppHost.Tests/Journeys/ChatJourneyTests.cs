using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace EssentialCSharp.Web.AppHost.Tests.Journeys;

/// <summary>
/// Browser journeys through the standalone Web AppHost with a fake chat model: what a reader actually does to log in
/// and use the AI assistant. These run on PRs (Linux CI with containers) and catch regressions that HTTP-level tests
/// can't see — the Vue chat widget, cookies, antiforgery, captcha wiring, and streaming.
/// </summary>
[Category("Docker")]
[Category("Journey")]
[RequiresLinuxContainers]
// All requests come from 127.0.0.1, and anonymous ones (login pages, smoke tests) share one global limit of
// ~30/min. Running journeys one at a time keeps bursts well under it; each journey takes a few seconds.
[NotInParallel(nameof(ChatJourneyTests))]
[ClassDataSource<JourneyFixture>(Shared = SharedType.PerTestSession)]
public class ChatJourneyTests(JourneyFixture fixture) : PageTest
{
    // Pre-accepting cookie consent keeps the banner from covering the chat button.
    private const string ConsentCookieValue = "%7B%22analytics_storage%22%3A%22granted%22%2C%22_version%22%3A%223%22%7D";

    [Before(Test)]
    public async Task AcceptCookieConsent()
    {
        await Context.AddCookiesAsync(
        [
            new Cookie { Name = "essential-csharp-consent", Value = ConsentCookieValue, Url = fixture.WebUrl.GetLeftPart(UriPartial.Authority) },
        ]);
    }

    [Test]
    public async Task AnonymousReader_OpeningChat_IsAskedToLogIn()
    {
        await GoToAsync("/home");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Login required for AI Chat" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Login Required" })).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Go to login page" }))
            .ToHaveAttributeAsync("href", "/Identity/Account/Login");
    }

    [Test]
    public async Task Reader_CanLogInAndLogOut()
    {
        TestUser user = await fixture.CreateConfirmedUserAsync("login");

        await LogInAsync(user);
        await Page.Locator("#logout").ClickAsync();

        await Expect(Page.Locator("#login")).ToBeVisibleAsync();
        await Expect(Page.Locator("#logout")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task LoggedInReader_AskingAQuestion_GetsTheModelsReply()
    {
        TestUser user = await fixture.CreateConfirmedUserAsync("chat");
        string question = $"What is a class? ({Guid.NewGuid():N})";

        await LogInAsync(user);
        await SendChatMessageAsync(question);

        await Expect(Page.Locator(".message-bubble.assistant .message-content"))
            .ToContainTextAsync(FakeChatModelServer.CannedReply);
        await Assert.That(fixture.ChatModel.ReceivedPrompts.Any(prompt => prompt.Contains(question, StringComparison.Ordinal))).IsTrue()
            .Because("the reader's question should reach the model unchanged");
    }

    [Test]
    public async Task LoggedInReader_WhenTheModelFails_SeesAnError()
    {
        TestUser user = await fixture.CreateConfirmedUserAsync("chat-error");

        await LogInAsync(user);
        await SendChatMessageAsync($"Please fail {FakeChatModelServer.FailTrigger}");

        // The same heading element shows rate-limit, auth, captcha and validation errors under other titles, so
        // require the generic "Error" title *and* proof the request reached the model; otherwise a 429 or an auth
        // regression would make this pass without exercising the model-failure path at all.
        await Expect(Page.Locator(".message-text h4")).ToHaveTextAsync("Error");
        await Expect(Page.Locator(".message-text")).ToContainTextAsync("AI assistant is temporarily unavailable");
        await Assert.That(fixture.ChatModel.ReceivedPrompts.Any(prompt => prompt.Contains(FakeChatModelServer.FailTrigger, StringComparison.Ordinal))).IsTrue()
            .Because("the failure should come from the model, not from something before it");
        await Expect(Page.Locator(".message-bubble.assistant", new() { HasText = FakeChatModelServer.CannedReply })).ToHaveCountAsync(0);
    }

    private Task<IResponse?> GoToAsync(string path) => Page.GotoAsync(new Uri(fixture.WebUrl, path).ToString());

    private async Task LogInAsync(TestUser user)
    {
        await GoToAsync("/Identity/Account/Login");
        await Page.Locator("#Input_Email").FillAsync(user.Email);
        await Page.Locator("#Input_Password").FillAsync(user.Password);
        // hCaptcha's public test keys (the Development default) pass the invisible challenge automatically, but
        // still need network access to hcaptcha.com (browser script + server-side siteverify). If hCaptcha is
        // unreachable, login shows "Human verification failed" and this wait times out.
        await Page.Locator("#login-submit").ClickAsync();

        await Expect(Page.Locator("#logout")).ToBeVisibleAsync(new() { Timeout = 30_000 });
    }

    private async Task SendChatMessageAsync(string message)
    {
        await GoToAsync("/home");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Open AI Chat Assistant" }).ClickAsync();
        await Page.Locator("#chat-input").FillAsync(message);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send message" }).ClickAsync();
    }
}
