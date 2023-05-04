using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web;

public sealed class Program
{
    private static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorPages();

        builder.Services.AddSingleton<Services.SiteMappingService>();

        WebApplication app = builder.Build();
        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();
        app.UseAuthorization();

        app.MapDefaultControllerRoute();

        app.MapControllerRoute(
            name: "slug",
            pattern: "{*key}",
            defaults: new { controller = "Home", action = "Index" });

        app.Run();
    }
}
