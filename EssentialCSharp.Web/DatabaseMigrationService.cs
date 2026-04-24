using EssentialCSharp.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web;

/// <summary>
/// Runs EF Core database migrations on application startup. Must be registered before
/// any hosted service that reads the database (e.g. <c>DataProtectionHostedService</c>),
/// because <c>IHostedService</c> instances start in DI registration order.
/// </summary>
public class DatabaseMigrationService(IServiceScopeFactory services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = services.CreateScope();
        EssentialCSharpWebContext context = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
