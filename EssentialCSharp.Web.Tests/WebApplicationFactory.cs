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
        $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";

    private readonly SemaphoreSlim _schemaInitializationGate = new(1, 1);

    // Kept open for the factory lifetime so the shared-cache in-memory database is not dropped
    // when per-scope connections are disposed between requests.
    private readonly SqliteConnection _keepAliveConnection;

    public WebApplicationFactory()
    {
        _keepAliveConnection = new SqliteConnection(_sqlConnectionString);
        _keepAliveConnection.Open();
    }

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

            // Keep per-scope connections so each request/test scope uses a fresh DbConnection,
            // which avoids locking issues from sharing one SqliteConnection instance.
            services.AddScoped<DbConnection>(_ =>
            {
                SqliteConnection conn = new(_sqlConnectionString);
                try
                {
                    conn.Open();
                }
                catch
                {
                    conn.Dispose();
                    throw;
                }
                return conn;
            });

            // The scoped DI container owns this DbConnection instance and disposes it at
            // scope end; EF Core treats it as externally owned when passed via UseSqlite.
            services.AddDbContext<EssentialCSharpWebContext>((serviceProvider, options) =>
            {
                DbConnection dbConnection = serviceProvider.GetRequiredService<DbConnection>();
                options.UseSqlite(dbConnection);
            });

            // Ensure schema exists before hosted services by prepending this registration
            // at index 0 of the service collection.
            RemoveSingleOrNone(
                services,
                descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                    descriptor.ImplementationType == typeof(EnsureCreatedHostedService),
                nameof(EnsureCreatedHostedService));

            ServiceDescriptor ensureCreatedDescriptor =
                ServiceDescriptor.Singleton<IHostedService>(
                    serviceProvider => new EnsureCreatedHostedService(
                        serviceProvider,
                        _schemaInitializationGate));
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
            _keepAliveConnection.Dispose();
            _schemaInitializationGate.Dispose();
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

    private sealed class EnsureCreatedHostedService(
        IServiceProvider serviceProvider,
        SemaphoreSlim schemaInitializationGate) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            await schemaInitializationGate.WaitAsync(cancellationToken);
            try
            {
                using IServiceScope scope = serviceProvider.CreateScope();
                EssentialCSharpWebContext dbContext =
                    scope.ServiceProvider.GetRequiredService<EssentialCSharpWebContext>();
                await dbContext.Database.EnsureCreatedAsync(cancellationToken);
            }
            finally
            {
                schemaInitializationGate.Release();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
