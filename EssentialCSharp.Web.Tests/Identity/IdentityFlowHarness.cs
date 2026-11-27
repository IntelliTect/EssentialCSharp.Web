using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests.Identity;

/// <summary>
/// Drives the Identity pages over HTTP the way separate people in separate browsers would, with email captured
/// instead of sent. Used by the account-takeover regression tests.
/// </summary>
internal sealed class IdentityFlowHarness
{
    public const string ApplicationCookieName = ".AspNetCore.Identity.Application";

    // Not in the bundled top-100,000 list, and the Have I Been Pwned lookup is stubbed out.
    public const string StrongPassword = "Quiet-Harbor-Lantern-4821";
    public const string OtherStrongPassword = "Copper-Meadow-Violin-7305";

    private readonly Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> _factory;

    public IdentityFlowHarness(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> baseFactory, string? hostHeader = null)
    {
        _factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            services.AddHttpClient("HaveIBeenPwned").ConfigurePrimaryHttpMessageHandler(() => new NotPwnedHandler());
        }));
        HostHeader = hostHeader;
    }

    public RecordingEmailSender Email { get; } = new();

    public IServiceProvider Services => _factory.Services;

    private string? HostHeader { get; }

    public static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    public Browser NewBrowser() => new(
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false }),
        HostHeader);

    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using IServiceScope scope = Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public Task<EssentialCSharpWebUser?> FindUserAsync(string email) =>
        InScopeAsync(sp => sp.GetRequiredService<UserManager<EssentialCSharpWebUser>>().FindByEmailAsync(email));

    public Task<IList<UserLoginInfo>> GetLoginsAsync(string email) => InScopeAsync(async sp =>
    {
        UserManager<EssentialCSharpWebUser> users = sp.GetRequiredService<UserManager<EssentialCSharpWebUser>>();
        EssentialCSharpWebUser user = await users.FindByEmailAsync(email) ?? throw new InvalidOperationException($"No user {email}.");
        return await users.GetLoginsAsync(user);
    });

    /// <summary>Creates a confirmed account directly, optionally with a password.</summary>
    public Task<string> CreateConfirmedUserAsync(string email, string? password) => InScopeAsync(async sp =>
    {
        UserManager<EssentialCSharpWebUser> users = sp.GetRequiredService<UserManager<EssentialCSharpWebUser>>();
        EssentialCSharpWebUser user = new() { UserName = email, Email = email, EmailConfirmed = true };
        IdentityResult result = password is null ? await users.CreateAsync(user) : await users.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join("; ", result.Errors.Select(e => e.Description)));
        }
        return user.Id;
    });

    /// <summary>
    /// Builds the cookie the external-login middleware sets after a successful GitHub/Microsoft sign-in, standing in
    /// for a real OAuth round trip.
    /// </summary>
    public (string Name, string Value) CreateExternalLoginCookie(string provider, string providerKey)
    {
        using IServiceScope scope = Services.CreateScope();
        CookieAuthenticationOptions options = scope.ServiceProvider
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ExternalScheme);
        ClaimsIdentity identity = new(
            [new Claim(ClaimTypes.NameIdentifier, providerKey), new Claim(ClaimTypes.Name, providerKey)],
            provider);
        AuthenticationProperties properties = new()
        {
            IssuedUtc = DateTimeOffset.UtcNow,
            ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(5),
        };
        properties.Items["LoginProvider"] = provider;
        AuthenticationTicket ticket = new(new ClaimsPrincipal(identity), properties, IdentityConstants.ExternalScheme);
        return (options.Cookie.Name!, options.TicketDataFormat.Protect(ticket));
    }

    /// <summary>Returns the path and query of the link in the last email with this subject sent to this address.</summary>
    public string LinkFromEmail(string to, string subject)
    {
        string body = Email.Last(to, subject).Body;
        Match match = Regex.Match(body, "href='([^']+)'");
        if (!match.Success)
        {
            throw new InvalidOperationException($"No link in '{subject}' email to {to}.");
        }
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    public static string PathAndQuery(string absoluteLink) => new Uri(absoluteLink).PathAndQuery;

    /// <summary>Extracts and decodes the <c>code</c> query value of an emailed reset link.</summary>
    public static string DecodeCode(string link)
    {
        string code = Regex.Match(link, "[?&]code=([^&]+)").Groups[1].Value;
        return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(WebUtility.UrlDecode(code)));
    }

    /// <summary>Resets the password through the ForgotPassword and ResetPassword pages.</summary>
    public async Task ResetPasswordThroughPagesAsync(Browser browser, string email, string newPassword)
    {
        using HttpResponseMessage forgot = await browser.PostFormAsync("/Identity/Account/ForgotPassword", new() { ["Input.Email"] = email });
        string resetLink = LinkFromEmail(email, "Reset Password");
        using HttpResponseMessage reset = await browser.PostFormAsync(
            "/Identity/Account/ResetPassword",
            new()
            {
                ["Input.Email"] = email,
                ["Input.Password"] = newPassword,
                ["Input.ConfirmPassword"] = newPassword,
                ["Input.Code"] = DecodeCode(resetLink),
            },
            getUrl: PathAndQuery(resetLink));
        if (reset.Headers.Location?.OriginalString.Contains("ResetPasswordConfirmation", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException($"Password reset failed: {(int)reset.StatusCode} {reset.Headers.Location}");
        }
    }

    internal sealed class RecordingEmailSender : IEmailSender
    {
        private readonly List<(string To, string Subject, string Body)> _Sent = [];

        public IReadOnlyList<(string To, string Subject, string Body)> Sent
        {
            get { lock (_Sent) { return [.. _Sent]; } }
        }

        public Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            lock (_Sent) { _Sent.Add((email, subject, htmlMessage)); }
            return Task.CompletedTask;
        }

        public (string To, string Subject, string Body) Last(string to, string subject) =>
            Sent.Last(m => m.To == to && m.Subject == subject);

        public int CountTo(string to) => Sent.Count(m => m.To == to);
    }

    private sealed class NotPwnedHandler : HttpMessageHandler
    {
        // The response is handed to the HttpClient pipeline; whoever made the request disposes it.
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(CreateNoMatchesResponse());

        private static HttpResponseMessage CreateNoMatchesResponse()
        {
            HttpResponseMessage response = new(HttpStatusCode.OK);
            response.Content = new StringContent(string.Empty);
            return response;
        }
    }
}

/// <summary>One person's browser: keeps its own cookies and fills in antiforgery and captcha fields.</summary>
internal sealed class Browser(HttpClient client, string? hostHeader)
{
    private readonly Dictionary<string, string> _Cookies = new(StringComparer.Ordinal);

    public bool HasCookie(string name) => _Cookies.ContainsKey(name);

    public void SetCookie(string name, string value) => _Cookies[name] = value;

    public void SetCookie((string Name, string Value) cookie) => _Cookies[cookie.Name] = cookie.Value;

    public Task<HttpResponseMessage> GetAsync(string url) => SendAsync(HttpMethod.Get, url);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, Dictionary<string, string>? form = null)
    {
        using HttpRequestMessage request = new(method, url);
        if (hostHeader is not null)
        {
            request.Headers.Host = hostHeader;
        }
        if (_Cookies.Count > 0)
        {
            request.Headers.Add("Cookie", string.Join("; ", _Cookies.Select(kv => $"{kv.Key}={kv.Value}")));
        }
        if (form is not null)
        {
            request.Content = new FormUrlEncodedContent(form);
        }

        HttpResponseMessage response = await client.SendAsync(request);
        if (response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values))
        {
            foreach (string header in values)
            {
                string pair = header.Split(';')[0];
                int separator = pair.IndexOf('=', StringComparison.Ordinal);
                string name = pair[..separator];
                string value = pair[(separator + 1)..];
                bool deleted = value.Length == 0 || header.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase);
                if (deleted)
                {
                    _Cookies.Remove(name);
                }
                else
                {
                    _Cookies[name] = value;
                }
            }
        }
        return response;
    }

    /// <summary>GETs the form page for its antiforgery token, then POSTs the form with a passing captcha response.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(
        string url,
        Dictionary<string, string> form,
        string? getUrl = null,
        string captchaResponse = "test-captcha-response")
    {
        using HttpResponseMessage page = await GetAsync(getUrl ?? url);
        string html = await page.Content.ReadAsStringAsync();
        Match token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        if (!token.Success)
        {
            throw new InvalidOperationException($"No antiforgery token on {getUrl ?? url} ({(int)page.StatusCode}).");
        }

        Dictionary<string, string> fields = new(form)
        {
            ["__RequestVerificationToken"] = token.Groups[1].Value,
            [CaptchaOptions.HttpPostResponseKeyName] = captchaResponse,
        };
        return await SendAsync(HttpMethod.Post, url, fields);
    }

    public async Task<bool> LogInWithPasswordAsync(string email, string password)
    {
        using HttpResponseMessage response = await PostFormAsync(
            "/Identity/Account/Login",
            new() { ["Input.Email"] = email, ["Input.Password"] = password });
        return HasCookie(IdentityFlowHarness.ApplicationCookieName);
    }
}
