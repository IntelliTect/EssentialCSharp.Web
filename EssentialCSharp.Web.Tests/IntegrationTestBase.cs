using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TUnit.AspNetCore;

namespace EssentialCSharp.Web.Tests;

public abstract class IntegrationTestBase : WebApplicationTest<WebApplicationFactory, Program>
{
    /// <summary>
    /// Executes a GET request and follows redirect responses while preserving
    /// TUnit trace correlation by using <see cref="TracedWebApplicationFactory{T}.CreateClient()"/>.
    /// This helper intentionally follows redirects using GET requests only.
    /// </summary>
    protected HttpClient CreateClientWithoutRedirectFollowing() =>
        Factory.Inner.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected static async Task<HttpResponseMessage> GetFollowingGetRedirectsAsync(
        HttpClient client,
        string relativeUrl,
        int maxRedirects = 10)
    {
        HttpResponseMessage response = await client.GetAsync(relativeUrl);

        for (int redirectCount = 0;
             redirectCount < maxRedirects && IsRedirectStatusCode(response.StatusCode);
             redirectCount++)
        {
            Uri? location = response.Headers.Location;
            if (location is null)
            {
                return response;
            }

            response.Dispose();

            response = await client.GetAsync(location);
        }

        if (IsRedirectStatusCode(response.StatusCode))
        {
            throw new InvalidOperationException(
                $"Exceeded redirect limit ({maxRedirects}) for '{relativeUrl}'. Last status: {(int)response.StatusCode} {response.StatusCode}.");
        }

        return response;
    }

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.Moved ||
        statusCode == HttpStatusCode.Found ||
        statusCode == HttpStatusCode.RedirectMethod ||
        statusCode == HttpStatusCode.TemporaryRedirect ||
        statusCode == HttpStatusCode.PermanentRedirect;

    public T InServiceScope<T>(Func<IServiceProvider, T> action)
    {
        using IServiceScope scope = Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return action(scope.ServiceProvider);
    }

    public void InServiceScope(Action<IServiceProvider> action)
    {
        using IServiceScope scope = Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        action(scope.ServiceProvider);
    }

    public async Task<T> InServiceScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        using IServiceScope scope = Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        return await action(scope.ServiceProvider);
    }

    public async Task InServiceScopeAsync(Func<IServiceProvider, Task> action)
    {
        using IServiceScope scope = Factory.Services.GetRequiredService<IServiceScopeFactory>().CreateScope();
        await action(scope.ServiceProvider);
    }
}
