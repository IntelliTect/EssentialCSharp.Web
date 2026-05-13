using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using TUnit.AspNetCore;

namespace EssentialCSharp.Web.Tests;

public abstract class IntegrationTestBase : WebApplicationTest<WebApplicationFactory, Program>
{
    /// <summary>
    /// Creates an HTTP client with redirect following enabled.
    /// NOTE: This bypasses TUnit trace correlation because <see cref="TracedWebApplicationFactory{T}"/>
    /// does not expose a <c>CreateClient(WebApplicationFactoryClientOptions)</c> overload.
    /// Use <see cref="TUnit.AspNetCore.TracedWebApplicationFactory{T}.CreateClient()"/> for all
    /// other tests where AllowAutoRedirect=false is acceptable.
    /// </summary>
    protected HttpClient CreateRedirectFollowingClient() =>
        Factory.Inner.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = true });

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
