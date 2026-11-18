using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;
using TUnit.Playwright;
using static Microsoft.Playwright.Assertions;

namespace EssentialCSharp.Web.AppHost.Tests.Journeys;

/// <summary>Browser journeys for logging in and using the AI chat, against the standalone AppHost with a fake model.</summary>
[Category("Docker")]
[Category("Journey")]
[RequiresLinuxContainers]
[CaptureOnFailure]
[ClassDataSource<JourneyFixture>(Shared = SharedType.PerTestSession)]
public class ChatJourneyTests(JourneyFixture fixture) : PageTest
{
    // Otherwise the consent banner covers the chat button.
    private const string ConsentCookieValue = "%7B%22analytics_storage%22%3A%22granted%22%2C%22_version%22%3A%223%22%7D";

    [Before(Test)]
    public async Task PrepareBrowser()
    {
        await Context.AddCookiesAsync(
        [
            new Cookie { Name = "essential-csharp-consent", Value = ConsentCookieValue, Url = fixture.WebUrl.GetLeftPart(UriPartial.Authority) },
        ]);
        await Context.RouteAsync("https://js.hcaptcha.com/**", route =>
            route.FulfillAsync(new() { ContentType = "application/javascript", Body = FakeHCaptcha.BrowserScript }));
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
    [Arguments("chat_unavailable", "AI assistant is temporarily unavailable")]
    [Arguments("context_limit_exceeded", "conversation has grown too long")]
    [Arguments(null, "AI assistant is temporarily unavailable")] // Stream ends without an error event or [DONE].
    public async Task LoggedInReader_WhenTheReplyFailsMidStream_KeepsThePartialReplyAndSeesAnError(string? errorCode, string expectedError)
    {
        TestUser user = await fixture.CreateConfirmedUserAsync("chat-midstream");
        await LogInAsync(user);
        string stream = ServerSentEvent(new { type = "text", data = "Partial reply" })
            + (errorCode is null ? "" : ServerSentEvent(new { type = "error", errorCode }));
        await Page.RouteAsync("**/api/chat/stream", route =>
            route.FulfillAsync(new() { Status = 200, ContentType = "text/event-stream", Body = stream }));

        await SendChatMessageAsync("What is a class?");

        await Expect(Page.Locator(".message-bubble.assistant .message-content")).ToContainTextAsync("Partial reply");
        await Expect(Page.Locator(".message-text")).ToContainTextAsync(expectedError);
        await Expect(Page.Locator(".typing-indicator")).ToHaveCountAsync(0);
    }

    private static string ServerSentEvent(object payload) => $"data: {JsonSerializer.Serialize(payload)}\n\n";


    /// <summary>
    /// Reloads when the app's own resources fail at the network level, e.g. net::ERR_NETWORK_CHANGED when
    /// container networks change on a Linux host. HTTP error responses still fail the test.
    /// </summary>
    private async Task<IResponse?> GoToAsync(string path)
    {
        const int maxAttempts = 3;
        string url = new Uri(fixture.WebUrl, path).ToString();
        string origin = fixture.WebUrl.GetLeftPart(UriPartial.Authority);
        var failures = new List<string>();

        for (int attempt = 1; ; attempt++)
        {
            var attemptFailures = new ConcurrentQueue<string>();
            void OnRequestFailed(object? sender, IRequest request)
            {
                if (request.Failure != "net::ERR_ABORTED" && request.Url.StartsWith(origin, StringComparison.OrdinalIgnoreCase))
                {
                    attemptFailures.Enqueue($"{request.Failure} {request.Url}");
                }
            }

            Page.RequestFailed += OnRequestFailed;
            IResponse? response = null;
            try
            {
                response = await Page.GotoAsync(url);
            }
            catch (PlaywrightException ex) when (attempt < maxAttempts && ex.Message.Contains("net::ERR_", StringComparison.Ordinal))
            {
                attemptFailures.Enqueue(ex.Message);
            }
            finally
            {
                Page.RequestFailed -= OnRequestFailed;
            }

            if (attemptFailures.IsEmpty)
            {
                return response;
            }

            failures.AddRange(attemptFailures.Select(failure => $"attempt {attempt}: {failure}"));
            if (attempt == maxAttempts)
            {
                throw new InvalidOperationException(
                    $"'{path}' kept failing to load after {maxAttempts} attempts:{Environment.NewLine}{string.Join(Environment.NewLine, failures)}");
            }

            Console.WriteLine($"Reloading '{path}' after network failures: {string.Join("; ", attemptFailures)}");
        }
    }

    private async Task LogInAsync(TestUser user)
    {
        await GoToAsync("/Identity/Account/Login");
        await Page.Locator("#Input_Email").FillAsync(user.Email);
        await Page.Locator("#Input_Password").FillAsync(user.Password);
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
