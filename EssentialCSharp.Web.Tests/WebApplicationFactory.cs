using EssentialCSharp.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

public sealed class WebApplicationFactory : WebApplicationFactory<Program>
{
    private static string SqlConnectionString => $"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
    private SqliteConnection? _Connection;
    private bool _databaseInitialized;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove all existing DbContext-related registrations to avoid provider conflicts in EF Core 10
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(EssentialCSharpWebContext) ||
                            d.ServiceType == typeof(DbContextOptions<EssentialCSharpWebContext>) ||
                            d.ServiceType == typeof(DbContextOptions))
                .ToList();

            foreach (var descriptor in descriptorsToRemove)
            {
                services.Remove(descriptor);
            }

            _Connection = new SqliteConnection(SqlConnectionString);
            _Connection.Open();

            // Add the test DbContext with SQLite
            services.AddDbContext<EssentialCSharpWebContext>(options =>
            {
                options.UseSqlite(_Connection);
            });
        });
    }

    /// <summary>
    /// Ensures the database is created. Called lazily on first access.
    /// </summary>
    private void EnsureDatabaseCreated()
    {
        if (_databaseInitialized) return;

        var factory = Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
        db.Database.EnsureCreated();
        _databaseInitialized = true;
    }

    /// <summary>
    /// Executes an action within a service scope, handling scope creation and cleanup automatically.
    /// </summary>
    /// <typeparam name="T">The return type of the action</typeparam>
    /// <param name="action">The action to execute with the scoped service provider</param>
    /// <returns>The result of the action</returns>
    public T InServiceScope<T>(Func<IServiceProvider, T> action)
    {
        EnsureDatabaseCreated();
        var factory = Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = factory.CreateScope();
        return action(scope.ServiceProvider);
    }

    /// <summary>
    /// Executes an action within a service scope, handling scope creation and cleanup automatically.
    /// </summary>
    /// <param name="action">The action to execute with the scoped service provider</param>
    public void InServiceScope(Action<IServiceProvider> action)
    {
        EnsureDatabaseCreated();
        var factory = Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = factory.CreateScope();
        action(scope.ServiceProvider);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _Connection?.Dispose();
            _Connection = null;
        }
    }
}
