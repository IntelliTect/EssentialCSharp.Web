using EssentialCSharp.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web;

public class DatabaseMigrationService(IServiceScopeFactory services) : BackgroundService
{
    public IServiceScopeFactory Services { get; } = services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using IServiceScope scope = Services.CreateScope();
        EssentialCSharpWebContext context = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
        await context.Database.MigrateAsync(stoppingToken);
    }
}
