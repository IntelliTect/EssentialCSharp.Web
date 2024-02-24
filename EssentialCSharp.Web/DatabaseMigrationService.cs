using EssentialCSharp.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web;

public class DatabaseMigrationService(IServiceProvider services) : BackgroundService
{
    public IServiceProvider Services { get; } = services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using IServiceScope scope = Services.CreateScope();
        EssentialCSharpWebContext? context = scope.ServiceProvider.GetService<EssentialCSharpWebContext>();
        if (context is null)
        {
            throw new InvalidOperationException($"EssentialCSharpWebContext not found for {nameof(DatabaseMigrationService)}");
        }
        await context.Database.MigrateAsync(stoppingToken);
    }
}
