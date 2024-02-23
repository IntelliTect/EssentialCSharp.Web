using EssentialCSharp.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web;

public class DatabaseMigrationService : BackgroundService
{
    public EssentialCSharpWebContext EssentialCSharpWebContext { get; }
    public DatabaseMigrationService(EssentialCSharpWebContext essentialCSharpWebContext)
    {
        EssentialCSharpWebContext = essentialCSharpWebContext;
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await EssentialCSharpWebContext.Database.MigrateAsync(stoppingToken);
    }
}
