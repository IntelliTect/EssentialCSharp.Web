using EssentialCSharp.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web;

public sealed class DatabaseMigrationService(IServiceScopeFactory services) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using IServiceScope scope = services.CreateScope();
        EssentialCSharpWebContext context = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
        await context.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
