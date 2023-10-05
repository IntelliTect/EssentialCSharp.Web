using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web;

public partial class Program
{
    private static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddEnvironmentVariables();

        // Add services to the container.
        builder.Services.AddRazorPages();

        builder.Services.AddSingleton<ICaptchaService, CaptchaService>();

        builder.Services.AddSingleton<ISiteMappingService, SiteMappingService>();

        builder.Services.AddHttpClient("hCaptcha", c =>
        {
            c.BaseAddress = new Uri("https://hcaptcha.com/");
        });

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
