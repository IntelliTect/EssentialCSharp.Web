using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace EssentialCSharp.Web.AppHost.Tests.Journeys;

/// <summary>Browser journeys for logging in and using the AI chat, against the standalone AppHost with a fake model.</summary>
[Category("Docker")]
[Category("Journey")]
[RequiresLinuxContainers]
[ClassDataSource<JourneyFixture>(Shared = SharedType.PerTestSession)]
public class ChatJourneyTests(JourneyFixture fixture) : PageTest
{
    // Otherwise the consent banner covers the chat button.
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

        // Rate-limit, auth and captcha errors render in the same element, so also prove the request reached the model.
        await Expect(Page.Locator(".message-text h4")).ToHaveTextAsync("Error");
        await Expect(Page.Locator(".message-text")).ToContainTextAsync("AI assistant is temporarily unavailable");
        await Assert.That(fixture.ChatModel.ReceivedPrompts.Any(prompt => prompt.Contains(FakeChatModelServer.FailTrigger, StringComparison.Ordinal))).IsTrue()
            .Because("the failure should come from the model, not from something before it");
        await Expect(Page.Locator(".message-bubble.assistant", new() { HasText = FakeChatModelServer.CannedReply })).ToHaveCountAsync(0);
    }

    // The local backend can't fail part-way through a reply, so these replay the server's stream events directly.
    [Test]
    [Arguments("""data: {"type":"error","errorCode":"chat_unavailable"}""", "AI assistant is temporarily unavailable")]
    [Arguments("""data: {"type":"error","errorCode":"context_limit_exceeded"}""", "conversation has grown too long")]
    [Arguments("", "AI assistant is temporarily unavailable")]
    public async Task LoggedInReader_WhenTheReplyFailsMidStream_KeepsThePartialReplyAndSeesAnError(string terminalEvent, string expectedError)
    {
        TestUser user = await fixture.CreateConfirmedUserAsync("chat-midstream");
        await LogInAsync(user);
        await Page.RouteAsync("**/api/chat/stream", route => route.FulfillAsync(new()
        {
            Status = 200,
            ContentType = "text/event-stream",
            Body = $"data: {{\"type\":\"text\",\"data\":\"Partial reply\"}}\n\n{terminalEvent}\n\n",
        }));

        await SendChatMessageAsync("What is a class?");

        await Expect(Page.Locator(".message-bubble.assistant .message-content")).ToContainTextAsync("Partial reply");
        await Expect(Page.Locator(".message-text")).ToContainTextAsync(expectedError);
        await Expect(Page.Locator(".typing-indicator")).ToHaveCountAsync(0);
    }

    private Task<IResponse?> GoToAsync(string path) => Page.GotoAsync(new Uri(fixture.WebUrl, path).ToString());

    private async Task LogInAsync(TestUser user)
    {
        await GoToAsync("/Identity/Account/Login");
        await Page.Locator("#Input_Email").FillAsync(user.Email);
        await Page.Locator("#Input_Password").FillAsync(user.Password);
        // Development's hCaptcha test keys pass automatically but need network access to hcaptcha.com.
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
