using System.Data.Common;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Services;
using TUnit.Core.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EssentialCSharp.Web.Tests;

public sealed class WebApplicationFactory : WebApplicationFactory<Program>, IAsyncInitializer
{
    public Task InitializeAsync()
    {
        // Force eager server initialization before tests run.
        // This is thread-safe and prevents race conditions from parallel tests
        // calling CreateClient() concurrently during lazy init.
        _ = Server;
        return Task.CompletedTask;
    }

    private static string SqlConnectionString => $"DataSource=file:{Guid.NewGuid()}?mode=memory&cache=shared";
    private SqliteConnection? _Connection;

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

            _Connection = new SqliteConnection(SqlConnectionString);
            _Connection.Open();

            services.AddDbContext<EssentialCSharpWebContext>(options =>
            {
                options.UseSqlite(_Connection);
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

    /// <summary>
    /// Executes an action within a service scope, handling scope creation and cleanup automatically.
    /// </summary>
    /// <typeparam name="T">The return type of the action</typeparam>
    /// <param name="action">The action to execute with the scoped service provider</param>
    /// <returns>The result of the action</returns>
    public T InServiceScope<T>(Func<IServiceProvider, T> action)
    {
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
        var factory = Services.GetRequiredService<IServiceScopeFactory>();
        using var scope = factory.CreateScope();
        action(scope.ServiceProvider);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync().ConfigureAwait(false);
        if (_Connection != null)
        {
            await _Connection.DisposeAsync().ConfigureAwait(false);
            _Connection = null;
        }
        GC.SuppressFinalize(this);
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
