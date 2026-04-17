using EssentialCSharp.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web;

/// <summary>
/// Runs EF Core migrations synchronously in <see cref="StartAsync"/> so the schema
/// is fully applied before the HTTP server begins accepting traffic. This prevents
/// race conditions where a request touches the DataProtectionKeys (or Identity) tables
/// before the migration that creates them has run.
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
