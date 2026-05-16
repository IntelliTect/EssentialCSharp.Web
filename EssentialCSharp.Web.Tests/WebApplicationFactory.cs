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
    private static string SqlConnectionString => $"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";

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

            services.AddSingleton<DbConnection>(_ => CreateOpenSqliteConnection());

            services.AddDbContext<EssentialCSharpWebContext>((serviceProvider, options) =>
            {
                DbConnection dbConnection = serviceProvider.GetRequiredService<DbConnection>();
                options.UseSqlite(dbConnection);
            });

            // Ensure schema exists before any other hosted service that reads from the database.
            services.Insert(0, ServiceDescriptor.Singleton<IHostedService, EnsureCreatedHostedService>());

            // Replace IListingSourceCodeService with one backed by TestData
            services.RemoveAll<IListingSourceCodeService>();
            services.AddSingleton<IListingSourceCodeService>(
                _ => TestListingSourceCodeServiceHelper.CreateService());
        });
    }

    private static SqliteConnection CreateOpenSqliteConnection()
    {
        SqliteConnection connection = new(SqlConnectionString);
        connection.Open();
        return connection;
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
