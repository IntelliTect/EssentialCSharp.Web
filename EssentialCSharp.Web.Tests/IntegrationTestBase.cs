using Microsoft.Extensions.DependencyInjection;
using TUnit.AspNetCore;

namespace EssentialCSharp.Web.Tests;

public abstract class IntegrationTestBase : WebApplicationTest<WebApplicationFactory, Program>
{
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
}
