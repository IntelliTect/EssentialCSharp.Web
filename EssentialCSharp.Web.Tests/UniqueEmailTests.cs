using System.Net;
using System.Text.RegularExpressions;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Regression tests for the duplicate-email race: two accounts must never share a NormalizedEmail (that makes every
/// FindByEmailAsync for the address throw), and losing the race is reported as "email taken", not a 500.
/// </summary>
public class UniqueEmailTests : IntegrationTestBase
{
    private const string Password = "Quiet-Harbor-Lantern-4821";

    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.NewGuid():N}@example.test";

    [Test]
    public async Task CreatingASecondAccountWithTheSameEmail_InTheStore_ReturnsDuplicateEmail()
    {
        string email = UniqueEmail("race");
        string normalizedEmail = email.ToUpperInvariant();
        (IdentityResult second, int rows, EssentialCSharpWebUser? found) = await InServiceScopeAsync(async services =>
        {
            UserManager<EssentialCSharpWebUser> users = services.GetRequiredService<UserManager<EssentialCSharpWebUser>>();
            await users.CreateAsync(new EssentialCSharpWebUser { UserName = $"first-{Guid.NewGuid():N}", Email = email });

            // The store skips Identity's "email taken" SELECT, just like a request that passed it at the same moment.
            IUserStore<EssentialCSharpWebUser> store = services.GetRequiredService<IUserStore<EssentialCSharpWebUser>>();
            EssentialCSharpWebUser duplicate = new()
            {
                UserName = $"second-{Guid.NewGuid():N}",
                NormalizedUserName = $"SECOND-{Guid.NewGuid():N}",
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString(),
            };
            IdentityResult result = await store.CreateAsync(duplicate, CancellationToken.None);

            int count = await services.GetRequiredService<EssentialCSharpWebContext>().Users.CountAsync(u => u.NormalizedEmail == normalizedEmail);
            return (result, count, await users.FindByEmailAsync(email));
        });

        using (Assert.Multiple())
        {
            await Assert.That(second.Succeeded).IsFalse();
            await Assert.That(second.Errors.Select(e => e.Code)).IsEquivalentTo(["DuplicateEmail"]);
            await Assert.That(rows).IsEqualTo(1);
            await Assert.That(found).IsNotNull();
        }
    }

    [Test]
    public async Task ChangingAnAccountToAnotherAccountsEmail_InTheStore_ReturnsDuplicateEmail()
    {
        string takenEmail = UniqueEmail("taken");
        string ownEmail = UniqueEmail("own");
        (IdentityResult result, string? storedEmail) = await InServiceScopeAsync(async services =>
        {
            UserManager<EssentialCSharpWebUser> users = services.GetRequiredService<UserManager<EssentialCSharpWebUser>>();
            await users.CreateAsync(new EssentialCSharpWebUser { UserName = $"a-{Guid.NewGuid():N}", Email = takenEmail });
            EssentialCSharpWebUser other = new() { UserName = $"b-{Guid.NewGuid():N}", Email = ownEmail };
            await users.CreateAsync(other);

            other.Email = takenEmail;
            other.NormalizedEmail = takenEmail.ToUpperInvariant();
            IdentityResult update = await services.GetRequiredService<IUserStore<EssentialCSharpWebUser>>().UpdateAsync(other, CancellationToken.None);

            string? email = await services.GetRequiredService<EssentialCSharpWebContext>().Users
                .Where(u => u.Id == other.Id).Select(u => u.Email).SingleAsync();
            return (update, email);
        });

        using (Assert.Multiple())
        {
            await Assert.That(result.Errors.Select(e => e.Code)).IsEquivalentTo(["DuplicateEmail"]);
            await Assert.That(storedEmail).IsEqualTo(ownEmail);
        }
    }

    [Test]
    public async Task ConcurrentRegisterPosts_ForOneEmail_CreateOneAccountAndNoServerErrors()
    {
        // The race is probabilistic (the audit won about 1 attempt in 7), so try several times.
        List<int> rowsPerAttempt = [];
        List<HttpStatusCode> statuses = [];
        foreach (int concurrency in new[] { 2, 2, 4, 4, 8, 8, 8, 8 })
        {
            (int rows, HttpStatusCode[] attemptStatuses) = await RaceRegistrationsAsync(concurrency);
            rowsPerAttempt.Add(rows);
            statuses.AddRange(attemptStatuses);
        }

        using (Assert.Multiple())
        {
            await Assert.That(rowsPerAttempt).All().Satisfy(rows => rows.IsEqualTo(1));
            await Assert.That(statuses).DoesNotContain(HttpStatusCode.InternalServerError);
        }
    }

    private async Task<(int Rows, HttpStatusCode[] Statuses)> RaceRegistrationsAsync(int concurrency)
    {
        // A fresh host per attempt gets a fresh rate limiter; the database is shared.
        using WebApplicationFactory<Program> factory = Factory.Inner.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddHttpClient("HaveIBeenPwned").ConfigurePrimaryHttpMessageHandler(() => new NotPwnedHandler())));
        HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        using HttpResponseMessage page = await client.GetAsync("/Identity/Account/Register");
        string token = Regex.Match(await page.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        // Warm up so page compilation and model building aren't part of the race.
        using (HttpResponseMessage warmUp = await client.PostAsync("/Identity/Account/Register", RegisterForm(token, $"warm-{Guid.NewGuid():N}", UniqueEmail("warm"))))
        {
        }

        string email = UniqueEmail("race");
        string normalizedEmail = email.ToUpperInvariant();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<HttpStatusCode>[] posts = [.. Enumerable.Range(0, concurrency).Select(i => Task.Run(async () =>
        {
            using FormUrlEncodedContent form = RegisterForm(token, $"racer{i}-{Guid.NewGuid():N}", email);
            await start.Task;
            using HttpResponseMessage response = await client.PostAsync("/Identity/Account/Register", form);
            return response.StatusCode;
        }))];
        start.SetResult();
        HttpStatusCode[] statuses = await Task.WhenAll(posts);

        using IServiceScope scope = factory.Services.CreateScope();
        int rows = await scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>().Users
            .CountAsync(u => u.NormalizedEmail == normalizedEmail);
        return (rows, statuses);
    }

    private static FormUrlEncodedContent RegisterForm(string token, string userName, string email) => new(new Dictionary<string, string>
    {
        ["Input.UserName"] = userName,
        ["Input.FirstName"] = "Test",
        ["Input.LastName"] = "User",
        ["Input.Email"] = email,
        ["Input.Password"] = Password,
        ["Input.ConfirmPassword"] = Password,
        ["h-captcha-response"] = "test-captcha-response",
        ["__RequestVerificationToken"] = token,
    });

    private sealed class NotPwnedHandler : HttpMessageHandler
    {
        // The response goes to the HttpClient pipeline, which disposes it.
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(CreateResponse());

        private static HttpResponseMessage CreateResponse()
        {
            HttpResponseMessage response = new(HttpStatusCode.OK);
            response.Content = new StringContent(string.Empty);
            return response;
        }
    }
}
