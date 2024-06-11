using EssentialCSharp.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

internal sealed class WebApplicationFactory : WebApplicationFactory<Program>
{
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

            services.AddDbContext<EssentialCSharpWebContext>(options =>
            {
                options.UseSqlite("Data Source=:memory:");
            });

            using ServiceProvider serviceProvider = services.BuildServiceProvider();

            using IServiceScope scope = serviceProvider.CreateScope();
            IServiceProvider scopedServices = scope.ServiceProvider;
            EssentialCSharpWebContext db = scopedServices.GetRequiredService<EssentialCSharpWebContext>();

            db.Database.OpenConnection();
            db.Database.EnsureCreated();
        });
    }
}
