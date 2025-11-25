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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext and related services
            var descriptorType = typeof(DbContextOptions<EssentialCSharpWebContext>);
            var descriptor = services.SingleOrDefault(d => d.ServiceType == descriptorType);
            if (descriptor != null)
            {
                services.Remove(descriptor);
            }

            // Remove all DbContextOptions related services to avoid EF Core 10 multiple provider error
            var allDbContextOptions = services.Where(d => 
                d.ServiceType.IsGenericType && 
                d.ServiceType.Name.Contains("DbContextOptions")).ToList();
            foreach (var desc in allDbContextOptions)
            {
                services.Remove(desc);
            }

            _Connection = new SqliteConnection(SqlConnectionString);
            _Connection.Open();

            // Add SQLite DbContext without using the global service provider
            services.AddDbContext<EssentialCSharpWebContext>(options =>
            {
                options.UseSqlite(_Connection);
                // Disable service provider caching to avoid shared state in EF Core 10
                options.EnableServiceProviderCaching(false);
            }, ServiceLifetime.Scoped, ServiceLifetime.Scoped);

            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            using IServiceScope scope = serviceProvider.CreateScope();
            IServiceProvider scopedServices = scope.ServiceProvider;
            EssentialCSharpWebContext db = scopedServices.GetRequiredService<EssentialCSharpWebContext>();

            db.Database.EnsureCreated();
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
