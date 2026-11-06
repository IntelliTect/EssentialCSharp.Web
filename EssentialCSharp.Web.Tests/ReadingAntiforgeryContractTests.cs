using System.Net;
using System.Text;
using EssentialCSharp.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// The reading client treats a rejected antiforgery token differently from a body that fails validation
/// ("stop tracking until the page reloads" versus "drop this interval"), so the two must stay distinguishable:
/// a write without a valid antiforgery token is rejected, changes nothing, and the rejection is a problem
/// document that carries no validation <c>errors</c> member.
/// </summary>
public class ReadingAntiforgeryContractTests : IntegrationTestBase
{
    private const long SeededWords = 1200;
    private const long SeededSeconds = 360;
    private const string ValidBody = $$"""{"intervals":[{"pageKey":"{{ReadingTestSupport.LargePage}}","wordsRead":10,"activeSeconds":10}]}""";
    private const string InvalidBody = """{"intervals":[]}""";

    protected override void ConfigureTestServices(IServiceCollection services) =>
        ReadingTestSupport.UseFakes(services, new ReadingTestSupport.FakeWordCountService());

    [Test]
    [Arguments("missing token")]
    [Arguments("invalid token")]
    public async Task WriteWithoutAValidAntiforgeryToken_IsRejectedAsNonValidationProblemAndLeavesProfileUnchanged(string tokenKind)
    {
        (string userId, ReadingTestSupport.AntiforgeryCredentials credentials) = await CreateUserWithCredentialsAsync();
        string? token = tokenKind == "invalid token" ? "not-a-valid-token" : null;

        using HttpResponseMessage response = await PostSessionAsync(credentials, token, ValidBody);
        ReadingTestSupport.ProblemResponse problem = await ReadingTestSupport.ReadProblemAsync(response);

        using (Assert.Multiple())
        {
            await Assert.That(problem.Status).IsEqualTo(HttpStatusCode.BadRequest).Because(problem.ToString());
            await Assert.That(problem.MediaType).IsEqualTo("application/problem+json").Because(problem.ToString());
            await Assert.That(problem.Root).IsNotNull().Because($"the body is not JSON: {problem}");
            await Assert.That(problem.HasErrorsMember).IsFalse()
                .Because($"an antiforgery rejection must not look like a validation failure: {problem}");
            await Assert.That(await ReadTotalsAsync(userId)).IsEqualTo((SeededWords, SeededSeconds));
        }
    }

    [Test]
    public async Task ValidationFailureWithAValidToken_CarriesAnErrorsMember_UnlikeAnAntiforgeryRejection()
    {
        (string userId, ReadingTestSupport.AntiforgeryCredentials credentials) = await CreateUserWithCredentialsAsync();

        using HttpResponseMessage response = await PostSessionAsync(credentials, credentials.RequestToken, InvalidBody);
        ReadingTestSupport.ProblemResponse problem = await ReadingTestSupport.ReadProblemAsync(response);

        using (Assert.Multiple())
        {
            await Assert.That(problem.Status).IsEqualTo(HttpStatusCode.BadRequest).Because(problem.ToString());
            await Assert.That(problem.Root).IsNotNull().Because($"the body is not JSON: {problem}");
            await Assert.That(problem.HasErrorsMember).IsTrue().Because(problem.ToString());
            await Assert.That(await ReadTotalsAsync(userId)).IsEqualTo((SeededWords, SeededSeconds));
        }
    }

    [Test]
    public async Task WriteWithAValidAntiforgeryToken_IsAccepted()
    {
        (string userId, ReadingTestSupport.AntiforgeryCredentials credentials) = await CreateUserWithCredentialsAsync();

        using HttpResponseMessage response = await PostSessionAsync(credentials, credentials.RequestToken, ValidBody);

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        await Assert.That(await ReadTotalsAsync(userId)).IsEqualTo((SeededWords + 10, SeededSeconds + 10));
    }

    private async Task<(string UserId, ReadingTestSupport.AntiforgeryCredentials Credentials)> CreateUserWithCredentialsAsync()
    {
        string userId = await McpTestHelper.CreateUserAsync(Factory, "reading-csrf");
        await ReadingTestSupport.SeedProfileAsync(Factory.Services, userId, SeededWords, SeededSeconds);
        return (userId, await ReadingTestSupport.CreateSignedInAntiforgeryCredentialsAsync(Factory, userId));
    }

    private async Task<HttpResponseMessage> PostSessionAsync(
        ReadingTestSupport.AntiforgeryCredentials credentials, string? token, string body)
    {
        using HttpClient client = McpTestHelper.CreateClient(Factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/reading/session")
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
