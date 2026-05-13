using System.Collections.Concurrent;
using System.Data.Common;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Services;
using TUnit.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EssentialCSharp.Web.Tests;

public sealed class WebApplicationFactory : TestWebApplicationFactory<Program>
{
    private static string SqlConnectionString => $"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";

    // Each per-test factory's ConfigureWebHost creates a new connection; track all for disposal.
    private readonly ConcurrentBag<SqliteConnection> _connections = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            ServiceDescriptor? dbContextDescriptor = services.SingleOrDefault(
                d => d.ServiceType ==
                    typeof(IDbContextOptionsConfiguration<EssentialCSharpWebContext>));

            if (dbContextDescriptor != null)
            {
                services.Remove(dbContextDescriptor);
            }

            ServiceDescriptor? dbConnectionDescriptor =
                services.SingleOrDefault(
                    d => d.ServiceType ==
                    typeof(DbConnection));

            if (dbConnectionDescriptor != null)
            {
                services.Remove(dbConnectionDescriptor);
            }

            // Remove DatabaseMigrationService: it calls MigrateAsync which conflicts
            // with EnsureCreated() used below for the in-memory SQLite test database.
            ServiceDescriptor? migrationServiceDescriptor = services.SingleOrDefault(
                d => d.ImplementationType == typeof(DatabaseMigrationService));
            if (migrationServiceDescriptor != null)
            {
                services.Remove(migrationServiceDescriptor);
            }

            // Capture in a local variable so each per-test factory's closure binds
            // to its own connection, not to a shared field that gets overwritten.
            SqliteConnection connection = new(SqlConnectionString);
            connection.Open();
            _connections.Add(connection);

            services.AddDbContext<EssentialCSharpWebContext>(options =>
            {
                options.UseSqlite(connection);
            });

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            using IServiceScope scope = serviceProvider.CreateScope();
            IServiceProvider scopedServices = scope.ServiceProvider;
            EssentialCSharpWebContext db = scopedServices.GetRequiredService<EssentialCSharpWebContext>();

            db.Database.EnsureCreated();

            // Replace IListingSourceCodeService with one backed by TestData
            services.RemoveAll<IListingSourceCodeService>();
            services.AddSingleton<IListingSourceCodeService>(
                _ => TestListingSourceCodeServiceHelper.CreateService());
        });
    }

    private int _connectionsDisposed;

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        if (Interlocked.Exchange(ref _connectionsDisposed, 1) == 0)
        {
            foreach (SqliteConnection connection in _connections)
                await connection.DisposeAsync().ConfigureAwait(false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Interlocked.Exchange(ref _connectionsDisposed, 1) == 0)
        {
            foreach (SqliteConnection connection in _connections)
                connection.Dispose();
        }
    }
}
