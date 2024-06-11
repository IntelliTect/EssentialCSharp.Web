using EssentialCSharp.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web;

public class DatabaseMigrationService(IServiceScopeFactory services) : BackgroundService
{
    public IServiceScopeFactory Services { get; } = services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using IServiceScope scope = Services.CreateScope();
        EssentialCSharpWebContext? context = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>()
            ?? throw new InvalidOperationException($"EssentialCSharpWebContext not found for {nameof(DatabaseMigrationService)}");
        if (!context.Database.GetPendingMigrations().Contains("20231021170008_CreateIdentitySchema"))
        {
            await context.Database.MigrateAsync(stoppingToken);
        }
        else
        {
            await context.Database.EnsureCreatedAsync(cancellationToken: stoppingToken);
        }
    }
}
