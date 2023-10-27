using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Middleware;
using EssentialCSharp.Web.Services;
using Mailjet.Client;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web;

public partial class Program
{
    private static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        ConfigurationManager configuration = builder.Configuration;
        string connectionString = builder.Configuration.GetConnectionString("EssentialCSharpWebContextConnection") ?? throw new InvalidOperationException("Connection string 'EssentialCSharpWebContextConnection' not found.");

        builder.Services.AddDbContext<EssentialCSharpWebContext>(options => options.UseSqlServer(connectionString));
        builder.Services.AddDefaultIdentity<EssentialCSharpWebUser>(options =>
            {
                // Password settings
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 10;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequiredUniqueChars = 6;

                options.SignIn.RequireConfirmedEmail = true;
                options.SignIn.RequireConfirmedAccount = true;

                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
                options.Lockout.MaxFailedAccessAttempts = 3;

                //TODO: Implement IProtectedUserStore
                //options.Stores.ProtectPersonalData = true;
            })
            .AddEntityFrameworkStores<EssentialCSharpWebContext>()
             .AddPasswordValidator<UsernameOrEmailAsPasswordValidator<EssentialCSharpWebUser>>()
             .AddPasswordValidator<Top100000PasswordValidator<EssentialCSharpWebUser>>();

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables();

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
            options.SlidingExpiration = true;
        });

        if (builder.Environment.IsDevelopment())
        {
            builder.Services.AddHsts(options =>
            {
                options.Preload = true;
                options.MaxAge = TimeSpan.FromDays(365);
                options.IncludeSubDomains = true;
            });
        }
        builder.Services.Configure<PasswordHasherOptions>(option =>
        {
            // https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html#pbkdf2
            // Minimum recommended is currently 210,000 iterations for pdkdf2-sha512 as of October 27, 2023
            option.IterationCount = 300000;
        });

        //TODO: Implement the anti-forgery token with every POST/PUT request: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery

        builder.Services.AddTransient<IEmailSender, EmailSender>();
        builder.Services.Configure<AuthMessageSenderOptions>(builder.Configuration.GetSection(AuthMessageSenderOptions.AuthMessageSender));
        builder.Services.Configure<CaptchaOptions>(builder.Configuration.GetSection(CaptchaOptions.CaptchaSender));

        // Add services to the container.
        builder.Services.AddRazorPages();

        builder.Services.AddSingleton<ICaptchaService, CaptchaService>();

        builder.Services.AddSingleton<ISiteMappingService, SiteMappingService>();

        builder.Services.AddHttpClient("hCaptcha", c =>
        {
            c.BaseAddress = new Uri("https://api.hcaptcha.com");
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
            app.UseHsts();
            app.UseSecurityHeadersMiddleware(new SecurityHeadersBuilder()
                .AddDefaultSecurePolicy());
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
