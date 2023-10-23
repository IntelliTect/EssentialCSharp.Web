using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Areas.Identity.Data;
using Mailjet.Client;

namespace EssentialCSharp.Web;

public partial class Program
{
    private static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        ConfigurationManager configuration = builder.Configuration;
        string connectionString = builder.Configuration.GetConnectionString("EssentialCSharpWebContextConnection") ?? throw new InvalidOperationException("Connection string 'EssentialCSharpWebContextConnection' not found.");

        builder.Services.AddDbContext<EssentialCSharpWebContext>(options => options.UseSqlServer(connectionString));
        builder.Services.AddDefaultIdentity<EssentialCSharpWebUser>(options => options.SignIn.RequireConfirmedAccount = true).AddEntityFrameworkStores<EssentialCSharpWebContext>();
        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables();

        builder.Services.AddTransient<IEmailSender, EmailSender>();
        builder.Services.Configure<AuthMessageSenderOptions>(builder.Configuration.GetSection(AuthMessageSenderOptions.AuthMessageSender));
        builder.Services.ConfigureApplicationCookie(o => {
            o.ExpireTimeSpan = TimeSpan.FromDays(14);
            o.SlidingExpiration = true;
        });


        // Add services to the container.
        builder.Services.AddRazorPages();

        builder.Services.AddSingleton<ICaptchaService, CaptchaService>();

        builder.Services.AddSingleton<ISiteMappingService, SiteMappingService>();

        builder.Services.AddHttpClient("hCaptcha", c =>
        {
            c.BaseAddress = new Uri("https://hcaptcha.com/");
        });

        builder.Services.AddHttpClient<IMailjetClient, MailjetClient>(client =>
        {
            //set BaseAddress, MediaType, UserAgent
            client.SetDefaultSettings();

            client.UseBasicAuthentication(configuration["AuthMessageSender:APIKey"], configuration["AuthMessageSender:SecretKey"]);
        });

        builder.Services.AddAuthentication()
         .AddMicrosoftAccount(microsoftoptions =>
         {
             microsoftoptions.ClientId = configuration["authentication:microsoft:clientid"] ?? throw new InvalidOperationException("authentication:microsoft:clientid unexpectedly null");
             microsoftoptions.ClientSecret = configuration["authentication:microsoft:clientsecret"] ?? throw new InvalidOperationException("authentication:microsoft:clientsecret unexpectedly null");
         })
         .AddGitHub(o =>
         {
             o.ClientId = configuration["authentication:github:clientId"] ?? throw new InvalidOperationException("github:clientId unexpectedly null");
             o.ClientSecret = configuration["authentication:github:clientSecret"] ?? throw new InvalidOperationException("github:clientSecret unexpectedly null");
             o.CallbackPath = "/signin-github";

             // Grants access to read a user's profile data.
             // https://docs.github.com/en/developers/apps/building-oauth-apps/scopes-for-oauth-apps
             o.Scope.Add("read:user");
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
        app.MapRazorPages();

        app.Run();
    }
}
