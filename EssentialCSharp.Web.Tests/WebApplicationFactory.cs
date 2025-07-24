﻿using EssentialCSharp.Web.Data;
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
            ServiceDescriptor? descriptor = services.SingleOrDefault(
                d => d.ServiceType ==
                    typeof(DbContextOptions<EssentialCSharpWebContext>));

            if (descriptor != null)
            {
                services.Remove(descriptor);
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
