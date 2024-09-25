using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Middleware;
using EssentialCSharp.Web.Services;
using Mailjet.Client;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Sqids;

namespace EssentialCSharp.Web;

public partial class Program
{
    private static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        ConfigurationManager configuration = builder.Configuration;
        string connectionString = builder.Configuration.GetConnectionString("EssentialCSharpWebContextConnection") ?? throw new InvalidOperationException("Connection string 'EssentialCSharpWebContextConnection' not found.");

        builder.Logging.AddConsole();
        builder.Services.AddHealthChecks();

        builder.Services.AddDbContext<EssentialCSharpWebContext>(options => options.UseSqlServer(connectionString));
        builder.Services.AddDefaultIdentity<EssentialCSharpWebUser>(options =>
        {
            // Password settings
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = PasswordRequirementOptions.PasswordMinimumLength;
            options.Password.RequireDigit = PasswordRequirementOptions.RequireDigit;
            options.Password.RequireNonAlphanumeric = PasswordRequirementOptions.RequireNonAlphanumeric;
            options.Password.RequireUppercase = PasswordRequirementOptions.RequireUppercase;
            options.Password.RequireLowercase = PasswordRequirementOptions.RequireLowercase;
            options.Password.RequiredUniqueChars = PasswordRequirementOptions.RequiredUniqueChars;

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
            option.IterationCount = 500000;
        });
        builder.Services.AddScoped<IUserEmailStore<EssentialCSharpWebUser>>(provider =>
        {
            if (!provider.GetRequiredService<UserManager<EssentialCSharpWebUser>>().SupportsUserEmail)
            {
                throw new NotSupportedException("The default UI requires a user store with email support.");
            }
            return (IUserEmailStore<EssentialCSharpWebUser>)provider.GetRequiredService<IUserStore<EssentialCSharpWebUser>>();
        });

        builder.Services.AddScoped<IUserPasswordStore<EssentialCSharpWebUser>>(provider =>
        {
            if (provider.GetRequiredService<IUserStore<EssentialCSharpWebUser>>() is IUserPasswordStore<EssentialCSharpWebUser> userPasswordStore)
            {
                return userPasswordStore;
            }
            throw new NotSupportedException("The default UI requires a user store with password support.");
        });

        //TODO: Implement the anti-forgery token with every POST/PUT request: https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery

        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddTransient<IEmailSender, EmailSender>();
        }
        builder.Services.Configure<AuthMessageSenderOptions>(builder.Configuration.GetSection(AuthMessageSenderOptions.AuthMessageSender));

        // Add services to the container.
        builder.Services.AddRazorPages();
        builder.Services.AddCaptchaService(builder.Configuration.GetSection(CaptchaOptions.CaptchaSender));
        builder.Services.AddSingleton<ISiteMappingService, SiteMappingService>();
        builder.Services.AddHostedService<DatabaseMigrationService>();
        builder.Services.AddSingleton(new SqidsEncoder<int>(new()
        {
            // This is a shuffled version of the default alphabet so the id's are at least unique to this site.
            // This being open source, it will be easy to decode the ids, but these id's are not meant to be secure.
            Alphabet = "imx4BSz2Ys7GZLXDqT5IAkUOEnyvwbPKJtp13NWdeuH6rFfRhCcQogjaM8V09l",
            MinLength = 10,
        }));

        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddHttpClient<IMailjetClient, MailjetClient>(client =>
            {
                //set BaseAddress, MediaType, UserAgent
                client.SetDefaultSettings();

                client.UseBasicAuthentication(configuration["AuthMessageSender:APIKey"], configuration["AuthMessageSender:SecretKey"]);
            });
        }

        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddAuthentication()
             .AddMicrosoftAccount(microsoftoptions =>
             {
                 microsoftoptions.ClientId = configuration["authentication:microsoft:clientid"] ?? throw new InvalidOperationException("authentication:microsoft:clientid unexpectedly null");
                 microsoftoptions.ClientSecret = configuration["authentication:microsoft:clientsecret"] ?? throw new InvalidOperationException("authentication:microsoft:clientsecret unexpectedly null");
                 microsoftoptions.CallbackPath = "/signin-microsoft";
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
        }

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        });

        WebApplication app = builder.Build();

        app.Use((context, next) =>
        {
            context.Request.Scheme = "https";
            return next(context);
        });

        app.UseForwardedHeaders();

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseHsts();
            app.UseSecurityHeadersMiddleware(new SecurityHeadersBuilder()
                .AddDefaultSecurePolicy());
        }

        app.MapHealthChecks("/healthz");

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthentication();
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
