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
using Microsoft.Extensions.Hosting;

namespace EssentialCSharp.Web.Tests;

public sealed class WebApplicationFactory : TestWebApplicationFactory<Program>
{
    // One GUID per factory instance (field, not computed property) ensures each factory
    // gets its own isolated in-memory database and keeps a stable connection string.
    private readonly string _sqlConnectionString =
        $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared;Pooling=True";

    // Kept open for the factory lifetime so the shared-cache in-memory database is not dropped
    // when per-scope connections are disposed between requests.
    private SqliteConnection? _keepAliveConnection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            RemoveSingleOrNone(
                services,
                descriptor => descriptor.ServiceType ==
                    typeof(IDbContextOptionsConfiguration<EssentialCSharpWebContext>),
                "IDbContextOptionsConfiguration<EssentialCSharpWebContext>");

            RemoveSingleOrNone(
                services,
                descriptor => descriptor.ServiceType == typeof(DbConnection),
                nameof(DbConnection));

            // Remove DatabaseMigrationService: it calls MigrateAsync which conflicts
            // with EnsureCreated() used below for the in-memory SQLite test database.
            RemoveSingleOrNone(
                services,
                descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType == typeof(DatabaseMigrationService),
                nameof(DatabaseMigrationService));

            // Open a keep-alive connection to prevent the shared-cache in-memory database from
            // being dropped when per-scope connections are disposed between requests.
            _keepAliveConnection ??= new SqliteConnection(_sqlConnectionString);
            if (_keepAliveConnection.State != System.Data.ConnectionState.Open)
            {
                _keepAliveConnection.Open();
            }

            // Register as scoped so each request scope gets its own SqliteConnection,
            // preventing "database is locked" errors under concurrent requests.
            services.AddScoped<DbConnection>(_ =>
            {
                SqliteConnection conn = new(_sqlConnectionString);
                conn.Open();
                return conn;
            });

            // The scoped DI container owns this DbConnection instance and disposes it at
            // scope end; EF Core treats it as externally owned when passed via UseSqlite.
            services.AddDbContext<EssentialCSharpWebContext>((serviceProvider, options) =>
            {
                DbConnection dbConnection = serviceProvider.GetRequiredService<DbConnection>();
                options.UseSqlite(dbConnection);
            });

            // Ensure schema exists before any hosted service that reads from the database.
            RemoveSingleOrNone(
                services,
                descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType == typeof(EnsureCreatedHostedService),
                nameof(EnsureCreatedHostedService));

            ServiceDescriptor ensureCreatedDescriptor =
                ServiceDescriptor.Singleton<IHostedService, EnsureCreatedHostedService>();
            services.Insert(0, ensureCreatedDescriptor);

            // Replace IListingSourceCodeService with one backed by TestData
            services.RemoveAll<IListingSourceCodeService>();
            services.AddSingleton<IListingSourceCodeService>(
                _ => TestListingSourceCodeServiceHelper.CreateService());
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _keepAliveConnection?.Dispose();
        }
        base.Dispose(disposing);
    }

    private static void RemoveSingleOrNone(
        IServiceCollection services,
        Func<ServiceDescriptor, bool> predicate,
        string descriptorName)
    {
        List<int> matchIndexes = [];
        for (int i = 0; i < services.Count; i++)
        {
            if (predicate(services[i]))
            {
                matchIndexes.Add(i);
            }
        }

        if (matchIndexes.Count > 1)
        {
            throw new InvalidOperationException(
                $"Expected at most one '{descriptorName}' registration but found {matchIndexes.Count}.");
        }

        if (matchIndexes.Count == 1)
        {
            services.RemoveAt(matchIndexes[0]);
        }
    }

    private sealed class EnsureCreatedHostedService(IServiceProvider serviceProvider) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            using IServiceScope scope = serviceProvider.CreateScope();
            EssentialCSharpWebContext dbContext =
                scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
