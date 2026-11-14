using System.Net;
using System.Text;
using EssentialCSharp.Web.Controllers;
using EssentialCSharp.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// The reading client stops tracking when an antiforgery token is rejected but only drops the batch when the
/// body fails validation, so the two must be distinguishable: a rejected token is flagged with the explicit
/// <see cref="ReadingAntiforgeryFailureHeaderAttribute.HeaderName"/> header, a validation failure is not.
/// </summary>
public class ReadingAntiforgeryContractTests : IntegrationTestBase
{
    private const long SeededWords = ReadingTestSupport.BaselineWords;
    private const long SeededSeconds = ReadingTestSupport.BaselineSeconds;
    private const string SessionUrl = "/api/reading/session";
    private const string ResetUrl = "/api/reading/reset";
    private const string ValidBody = $$"""{"intervals":[{"pageKey":"{{ReadingTestSupport.LargePage}}","wordsRead":10,"activeSeconds":10}]}""";
    private const string InvalidBody = """{"intervals":[]}""";

    protected override void ConfigureTestServices(IServiceCollection services) =>
        ReadingTestSupport.UseFakes(services, new ReadingTestSupport.FakeWordCountService());

    [Test]
    [Arguments(SessionUrl, null)]
    [Arguments(SessionUrl, "not-a-valid-token")]
    [Arguments(ResetUrl, null)]
    [Arguments(ResetUrl, "not-a-valid-token")]
    public async Task WriteWithoutAValidAntiforgeryToken_IsRejectedWithTheExplicitSignalAndLeavesProfileUnchanged(string url, string? token)
    {
        (string userId, ReadingTestSupport.AntiforgeryCredentials credentials) = await CreateUserWithCredentialsAsync();

        using HttpResponseMessage response = await PostAsync(url, credentials, token, ValidBody);

        using (Assert.Multiple())
        {
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
            await Assert.That(AntiforgeryFailureSignal(response)).IsNotNull();
            await Assert.That(await ReadTotalsAsync(userId)).IsEqualTo((SeededWords, SeededSeconds));
        }
    }

    [Test]
    public async Task ValidationFailureWithAValidToken_HasNoAntiforgerySignalAndCarriesErrors()
    {
        (string userId, ReadingTestSupport.AntiforgeryCredentials credentials) = await CreateUserWithCredentialsAsync();

        using HttpResponseMessage response = await PostAsync(SessionUrl, credentials, credentials.RequestToken, InvalidBody);
        ReadingTestSupport.ProblemResponse problem = await ReadingTestSupport.ReadProblemAsync(response);

        using (Assert.Multiple())
        {
            await Assert.That(problem.Status).IsEqualTo(HttpStatusCode.BadRequest).Because(problem.ToString());
            await Assert.That(AntiforgeryFailureSignal(response)).IsNull();
            await Assert.That(problem.HasErrorsMember).IsTrue().Because(problem.ToString());
            await Assert.That(await ReadTotalsAsync(userId)).IsEqualTo((SeededWords, SeededSeconds));
        }
    }

    [Test]
    public async Task WriteWithAValidAntiforgeryToken_IsAccepted()
    {
        (string userId, ReadingTestSupport.AntiforgeryCredentials credentials) = await CreateUserWithCredentialsAsync();

        using HttpResponseMessage response = await PostAsync(SessionUrl, credentials, credentials.RequestToken, ValidBody);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await ReadTotalsAsync(userId)).IsEqualTo((SeededWords + 10, SeededSeconds + 10));
    }

    private static string? AntiforgeryFailureSignal(HttpResponseMessage response) =>
        response.Headers.TryGetValues(ReadingAntiforgeryFailureHeaderAttribute.HeaderName, out IEnumerable<string>? values)
            ? values.Single()
            : null;

    private async Task<(string UserId, ReadingTestSupport.AntiforgeryCredentials Credentials)> CreateUserWithCredentialsAsync()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-csrf");
        await ReadingTestSupport.SeedProfileAsync(Factory.Services, userId, SeededWords, SeededSeconds);
        return (userId, await ReadingTestSupport.CreateSignedInAntiforgeryCredentialsAsync(Factory, userId));
    }

    private async Task<HttpResponseMessage> PostAsync(
        string url, ReadingTestSupport.AntiforgeryCredentials credentials, string? token, string body)
    {
        using HttpClient client = McpTestHelper.CreateClient(Factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Cookie", credentials.CookieHeader);
        if (token is not null)
        {
            request.Headers.Add(credentials.HeaderName, token);
        }

        return await client.SendAsync(request);
    }

    private async Task<(long Words, long Seconds)> ReadTotalsAsync(string userId) =>
        await InServiceScopeAsync(async services =>
        {
            UserReadingProfileTotals totals = await services.GetRequiredService<EssentialCSharpWebContext>()
                .UserReadingProfiles.AsNoTracking()
                .Where(p => p.UserId == userId)
                .Select(p => new UserReadingProfileTotals(p.TotalWordsRead, p.TotalActiveSeconds))
                .SingleAsync();
            return (totals.Words, totals.Seconds);
        });

    private sealed record UserReadingProfileTotals(long Words, long Seconds);
}
