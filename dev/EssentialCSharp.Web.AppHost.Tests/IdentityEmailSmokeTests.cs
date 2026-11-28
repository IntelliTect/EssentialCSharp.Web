using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Aspire.Hosting.Testing;

namespace EssentialCSharp.Web.AppHost.Tests;

/// <summary>
/// Drives Identity's email flows end to end: Web sends through MailKit to the AppHost's MailPit container, and the
/// test reads each message back from MailPit's REST API and follows the link it contains.
/// </summary>
/// <remarks>
/// The shared <see cref="Journeys.JourneyFixture"/> points Web's hCaptcha verification at an in-process fake that accepts
/// any token, so like the other smoke tests these need no outbound network.
/// <para>
/// Web limits anonymous requests per IP, and every test client is 127.0.0.1. These tests make many anonymous form
/// requests, so they run in their own parallel group (after the browser journeys, which they would otherwise starve)
/// and wait out any 429 that the journeys' traffic in the same window leaves behind.
/// </para>
/// </remarks>
[Category("Docker")]
[RequiresLinuxContainers]
[ParallelGroup(nameof(IdentityEmailSmokeTests))]
[ClassDataSource<Journeys.JourneyFixture>(Shared = SharedType.PerTestSession)]
public partial class IdentityEmailSmokeTests(Journeys.JourneyFixture fixture)
{
    private const string CaptchaToken = "smoke-test-token";
    private static readonly TimeSpan MailTimeout = TimeSpan.FromSeconds(60);

    [Test]
    public async Task Register_ConfirmationEmail_LinkEnablesSignIn()
    {
        using var web = CreateWebClient();
        using var mailpit = fixture.CreateHttpClient("mailpit", "http");
        string email = NewEmail();
        string password = NewPassword();

        await RegisterAsync(web, email, password);
        bool signedInBeforeConfirming = await CanSignInAsync(email, password);
        string confirmLink = await GetLinkFromEmailAsync(mailpit, email, "Confirm your email");
        using (var confirmResponse = await web.GetAsync(new Uri(confirmLink)))
        {
            confirmResponse.EnsureSuccessStatusCode();
        }

        // RequireConfirmedEmail: the account is unusable until the emailed link is followed.
        await Assert.That(signedInBeforeConfirming).IsFalse();
        await Assert.That(await CanSignInAsync(email, password)).IsTrue();
    }

    [Test]
    public async Task ForgotPassword_ResetEmail_LinkReplacesPassword()
    {
        using var web = CreateWebClient();
        using var mailpit = fixture.CreateHttpClient("mailpit", "http");
        string email = NewEmail();
        string oldPassword = NewPassword();
        await RegisterAsync(web, email, oldPassword);
        string confirmLink = await GetLinkFromEmailAsync(mailpit, email, "Confirm your email");
        using (var confirmResponse = await web.GetAsync(new Uri(confirmLink)))
        {
            confirmResponse.EnsureSuccessStatusCode();
        }

        using (var forgotResponse = await PostFormAsync(web, "/Identity/Account/ForgotPassword", new()
        {
            ["Input.Email"] = email,
        }))
        {
            // Checked before polling: a rejected form redisplays (200) and sends no email, and waiting would hide why.
            await Assert.That(forgotResponse.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        }

        string resetLink = await GetLinkFromEmailAsync(mailpit, email, "Reset Password");
        string newPassword = NewPassword();
        using var resetPage = await web.GetAsync(new Uri(resetLink));
        resetPage.EnsureSuccessStatusCode();
        string resetHtml = await resetPage.Content.ReadAsStringAsync();
        Match code = ResetCodeInput().Match(resetHtml);
        if (!code.Success)
        {
            throw new InvalidOperationException(
                $"No Input.Code field found on the reset page ({(int)resetPage.StatusCode}) at {resetLink}.");
        }

        using var resetResponse = await PostFormAsync(web, resetLink, new()
        {
            ["Input.Code"] = WebUtility.HtmlDecode(code.Groups[1].Value),
            ["Input.Email"] = email,
            ["Input.Password"] = newPassword,
            ["Input.ConfirmPassword"] = newPassword,
        }, resetHtml);

        await Assert.That(await CanSignInAsync(email, newPassword)).IsTrue();
        await Assert.That(await CanSignInAsync(email, oldPassword)).IsFalse();
    }

    private HttpClient CreateWebClient()
    {
        // Own handler: the antiforgery cookie must round-trip, and redirects are asserted rather than followed.
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), AllowAutoRedirect = false };
        return new HttpClient(new RetryAfterRateLimitHandler { InnerHandler = handler }) { BaseAddress = fixture.WebUrl };
    }

    /// <summary>Retries a 429 once its <c>Retry-After</c> elapses; Web's anonymous limit is a fixed one-minute window.</summary>
    private sealed class RetryAfterRateLimitHandler : DelegatingHandler
    {
        private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(65);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            for (int attempt = 0; ; attempt++)
            {
                HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
                if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == 2)
                {
                    return response;
                }

                TimeSpan wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10);
                response.Dispose();
                await Task.Delay(wait < MaxWait ? wait : MaxWait, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Signs in with a fresh cookie jar, then reports whether an <c>[Authorize]</c> page is reachable. That proves
    /// the auth cookie works, independent of the login page's wording or redirect target.
    /// </summary>
    private async Task<bool> CanSignInAsync(string email, string password)
    {
        using var client = CreateWebClient();
        using var loginResponse = await PostFormAsync(client, "/Identity/Account/Login", new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
        });

        using var account = await client.GetAsync(new Uri("/Identity/Account/Manage", UriKind.Relative));
        return account.StatusCode == HttpStatusCode.OK;
    }

    private static async Task RegisterAsync(HttpClient web, string email, string password)
    {
        using var response = await PostFormAsync(web, "/Identity/Account/Register", new()
        {
            ["Input.UserName"] = email.Split('@')[0],
            ["Input.Email"] = email,
            ["Input.FirstName"] = "Smoke",
            ["Input.LastName"] = "Test",
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
        });

        // Success redirects; a rejected form redisplays with validation errors.
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            string body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Registration was not accepted ({(int)response.StatusCode}). Validation: " +
                string.Join(" | ", ValidationErrors().Matches(body).Select(m => WebUtility.HtmlDecode(m.Groups[1].Value.Trim()))));
        }
    }

    /// <summary>Loads the form page for its antiforgery token, then posts the fields with the captcha response.</summary>
    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient web, string path, Dictionary<string, string> fields, string? formHtml = null)
    {
        if (formHtml is null)
        {
            using var page = await web.GetAsync(new Uri(path, UriKind.RelativeOrAbsolute));
            page.EnsureSuccessStatusCode();
            formHtml = await page.Content.ReadAsStringAsync();
        }

        Match token = AntiforgeryInput().Match(formHtml);
        if (!token.Success)
        {
            throw new InvalidOperationException($"No antiforgery token found on {path}.");
        }

        fields["__RequestVerificationToken"] = WebUtility.HtmlDecode(token.Groups[1].Value);
        fields["h-captcha-response"] = CaptchaToken;
        using var content = new FormUrlEncodedContent(fields);
        return await web.PostAsync(new Uri(path, UriKind.RelativeOrAbsolute), content);
    }

    /// <summary>Polls MailPit until a message to <paramref name="email"/> with the subject arrives, then returns its link.</summary>
    private static async Task<string> GetLinkFromEmailAsync(HttpClient mailpit, string email, string subject)
    {
        // Mailpit search syntax: https://mailpit.axllent.org/docs/usage/search-filters/
        string query = Uri.EscapeDataString($"to:\"{email}\" subject:\"{subject}\"");
        using var timeout = new CancellationTokenSource(MailTimeout);
        MailPitMessage? message = null;
        try
        {
            while (message is null)
            {
                var search = await mailpit.GetFromJsonAsync<MailPitSearchResult>($"/api/v1/search?query={query}", timeout.Token);
                if (search?.Messages is [var newest, ..])
                {
                    message = await mailpit.GetFromJsonAsync<MailPitMessage>(
                        $"/api/v1/message/{Uri.EscapeDataString(newest.Id)}", timeout.Token);
                }
                else
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), timeout.Token);
                }
            }
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"No email with subject '{subject}' to {email} arrived in MailPit within {MailTimeout.TotalSeconds:0}s.", ex);
        }

        Match link = HrefAttribute().Match(message.Html ?? string.Empty);
        if (!link.Success)
        {
            throw new InvalidOperationException($"Email '{subject}' to {email} contained no link: {message.Html}");
        }

        return WebUtility.HtmlDecode(link.Groups[1].Value);
    }

    private static string NewEmail() => $"smoke-{Guid.NewGuid():N}@example.test";

    // Random, so it never matches the common-password or breached-password validators.
    private static string NewPassword() => $"Pw-{Guid.NewGuid():N}";

    [GeneratedRegex("""<input[^>]*name="__RequestVerificationToken"[^>]*value="([^"]+)""")]
    private static partial Regex AntiforgeryInput();

    [GeneratedRegex("""<input[^>]*name="Input\.Code"[^>]*value="([^"]*)""")]
    private static partial Regex ResetCodeInput();

    [GeneratedRegex("""href=['"]([^'"]+)['"]""")]
    private static partial Regex HrefAttribute();

    [GeneratedRegex("""<li>([^<]+)</li>""")]
    private static partial Regex ValidationErrors();

    private sealed record MailPitSearchResult(
        [property: JsonPropertyName("messages")] IReadOnlyList<MailPitMessageSummary> Messages);

    private sealed record MailPitMessageSummary([property: JsonPropertyName("ID")] string Id);

    private sealed record MailPitMessage([property: JsonPropertyName("HTML")] string? Html);
}
