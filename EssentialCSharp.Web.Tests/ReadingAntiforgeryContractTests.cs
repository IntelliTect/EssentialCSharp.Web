using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using EssentialCSharp.Web.Areas.Identity.Data;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Evidence for the client contract: what POST /api/reading/session returns for a missing or invalid antiforgery
/// token versus a body that fails validation, so the client can tell "stop tracking" from "drop this interval".
/// </summary>
public class ReadingAntiforgeryContractTests : IntegrationTestBase
{
    private sealed record Observed(HttpStatusCode Status, string? ContentType, string Body)
    {
        public override string ToString() => $"{(int)Status} content-type={ContentType ?? "<none>"} body={(Body.Length == 0 ? "<empty>" : Body)}";
    }

    private const string ValidBody = """{"intervals":[{"pageKey":"x","wordsRead":10,"activeSeconds":10}]}""";
    private const string InvalidBody = """{"intervals":[]}""";

    [Test]
    public async Task AntiforgeryFailure_IsDistinguishableFromValidationFailure()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-csrf");
        (string cookieHeader, string token, string headerName) = await CreateCredentialsAsync(userId);

        Observed missing = await PostAsync(cookieHeader, headerName, token: null, ValidBody);
        Observed invalid = await PostAsync(cookieHeader, headerName, token: "not-a-valid-token", ValidBody);
        Observed validationFailure = await PostAsync(cookieHeader, headerName, token, InvalidBody);

        Console.WriteLine($"missing token : {missing}");
        Console.WriteLine($"invalid token : {invalid}");
        Console.WriteLine($"valid token, invalid body : {validationFailure}");

        // Observed contract: all three are 400 application/problem+json. A validation failure carries an "errors"
        // object (and the title "One or more validation errors occurred."); an antiforgery failure is a bare
        // problem document with no "errors" member. A missing token and an invalid token look the same.
        using (Assert.Multiple())
        {
            await Assert.That(validationFailure.Status).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(HasErrorsMember(validationFailure)).IsTrue();

            foreach (Observed antiforgeryFailure in new[] { missing, invalid })
            {
                await Assert.That(antiforgeryFailure.Status).IsEqualTo(HttpStatusCode.BadRequest);
                await Assert.That(antiforgeryFailure.ContentType).StartsWith("application/problem+json");
                await Assert.That(HasErrorsMember(antiforgeryFailure)).IsFalse();
            }
        }
    }

    private static bool HasErrorsMember(Observed response)
    {
        using JsonDocument document = JsonDocument.Parse(response.Body);
        return document.RootElement.TryGetProperty("errors", out _);
    }

    private async Task<Observed> PostAsync(string cookieHeader, string headerName, string? token, string body)
    {
        using HttpClient client = McpTestHelper.CreateClient(Factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reading/session")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Cookie", cookieHeader);
        if (token is not null)
        {
            request.Headers.Add(headerName, token);
        }

        using HttpResponseMessage response = await client.SendAsync(request);
        MediaTypeHeaderValue? contentType = response.Content.Headers.ContentType;
        return new Observed(response.StatusCode, contentType?.ToString(), await response.Content.ReadAsStringAsync());
    }

    private async Task<(string CookieHeader, string Token, string HeaderName)> CreateCredentialsAsync(string userId)
    {
        (string cookieName, string cookieValue) =
            await McpTestHelper.CreateIdentityApplicationCookieAsync(Factory, userId);

        (string antiforgeryCookie, string requestToken, string headerName) = await InServiceScopeAsync(async services =>
        {
            EssentialCSharpWebUser user = await services.GetRequiredService<UserManager<EssentialCSharpWebUser>>()
                .FindByIdAsync(userId)
                ?? throw new InvalidOperationException($"User {userId} does not exist.");
            var context = new DefaultHttpContext
            {
                RequestServices = services,
                User = await services.GetRequiredService<IUserClaimsPrincipalFactory<EssentialCSharpWebUser>>()
                    .CreateAsync(user)
            };

            AntiforgeryTokenSet tokens = services.GetRequiredService<IAntiforgery>().GetAndStoreTokens(context);
            string antiforgeryCookieName = services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.Name
                ?? throw new InvalidOperationException("The antiforgery cookie name is not configured.");
            string cookie = context.Response.Headers.SetCookie
                .Select(value => value?.Split(';', 2)[0])
                .OfType<string>()
                .Single(value => value.StartsWith(antiforgeryCookieName + "=", StringComparison.Ordinal));
            return (
                cookie,
                tokens.RequestToken ?? throw new InvalidOperationException("No antiforgery request token."),
                tokens.HeaderName ?? "RequestVerificationToken");
        });

        return ($"{cookieName}={cookieValue}; {antiforgeryCookie}", requestToken, headerName);
    }
}
