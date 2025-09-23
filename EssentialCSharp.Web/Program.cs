using System.Threading.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using EssentialCSharp.Chat.Common.Extensions;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Helpers;
using EssentialCSharp.Web.Middleware;
using EssentialCSharp.Web.Services;
using EssentialCSharp.Web.Services.Referrals;
using Mailjet.Client;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace EssentialCSharp.Web;

public partial class Program
{
    private static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

            // Only loopback proxies are allowed by default.
            // Clear that restriction because forwarders are enabled by explicit 
            // configuration.
            options.KnownNetworks.Clear();
            options.KnownProxies.Clear();
        });

        ConfigurationManager configuration = builder.Configuration;
        string connectionString = builder.Configuration.GetConnectionString("EssentialCSharpWebContextConnection") ?? throw new InvalidOperationException("Connection string 'EssentialCSharpWebContextConnection' not found.");

        builder.Logging.AddConsole();
        builder.Services.AddHealthChecks();

        // Create a logger that's accessible throughout the entire method
        var loggerFactory = LoggerFactory.Create(loggingBuilder =>
            loggingBuilder.AddConsole().SetMinimumLevel(LogLevel.Information));
        var initialLogger = loggerFactory.CreateLogger<Program>();

        if (!builder.Environment.IsDevelopment())
        {
            // Configure Azure Application Insights with OpenTelemetry only if connection string is available
            var appInsightsConnectionString = builder.Configuration.GetConnectionString("ApplicationInsights")
                ?? builder.Configuration["ApplicationInsights:ConnectionString"];

            if (!string.IsNullOrEmpty(appInsightsConnectionString))
            {
                builder.Services.AddOpenTelemetry().UseAzureMonitor(
                    options =>
                    {
                        options.ConnectionString = appInsightsConnectionString;
                    });
                builder.Services.AddApplicationInsightsTelemetry();
                builder.Services.AddServiceProfiler();
            }
            else
            {
                initialLogger.LogWarning("Application Insights connection string not found. Telemetry collection will be disabled.");
            }
        }

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
            .AddUserSecrets<Program>()
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
        builder.Services.AddSingleton<IRouteConfigurationService, RouteConfigurationService>();
        builder.Services.AddHostedService<DatabaseMigrationService>();
        builder.Services.AddHostedService<SearchIndexingHostedService>();
        builder.Services.AddScoped<IReferralService, ReferralService>();

        // Add Typesense search services
        builder.Services.Configure<TypesenseOptions>(
            builder.Configuration.GetSection(TypesenseOptions.SectionName));
        builder.Services.AddHttpClient<ITypesenseSearchService, TypesenseSearchService>();
        builder.Services.AddScoped<IContentIndexingService, ContentIndexingService>();

        // Add AI Chat services
        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddAzureOpenAIServices(configuration);
        }

        // Add Rate Limiting for API endpoints
        builder.Services.AddRateLimiter(options =>
        {
            // Global rate limiter for authenticated users by username, anonymous by IP
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                var partitionKey = httpContext.User.Identity?.IsAuthenticated == true
                    ? httpContext.User.Identity.Name ?? "unknown-user"
                    : httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: partitionKey,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30, // requests per window
                        Window = TimeSpan.FromMinutes(1), // minute window
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0 // No queuing - immediate rejection for better UX
                    });
            });

            options.AddFixedWindowLimiter("ChatEndpoint", rateLimiterOptions =>
            {
                rateLimiterOptions.PermitLimit = 15; // chat messages per window (reasonable limit)
                rateLimiterOptions.Window = TimeSpan.FromMinutes(1); // minute window
                rateLimiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                rateLimiterOptions.QueueLimit = 0; // No queuing to make rate limiting immediate
            });

            options.AddFixedWindowLimiter("Anonymous", rateLimiterOptions =>
            {
                rateLimiterOptions.PermitLimit = 5; // requests per window for anonymous users
                rateLimiterOptions.Window = TimeSpan.FromMinutes(1);
                rateLimiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                rateLimiterOptions.QueueLimit = 0; // No queuing for anonymous users
            });

            options.AddFixedWindowLimiter("SearchEndpoint", rateLimiterOptions =>
            {
                rateLimiterOptions.PermitLimit = 50; // search requests per window (higher limit for search)
                rateLimiterOptions.Window = TimeSpan.FromMinutes(1); // minute window
                rateLimiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                rateLimiterOptions.QueueLimit = 0; // No queuing for immediate response
            });

            // Custom response when rate limit is exceeded
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.HttpContext.Request.Path.StartsWithSegments("/.well-known"))
                {
                    return;
                }
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.Headers.RetryAfter = "60";
                if (context.HttpContext.Request.Path.StartsWithSegments("/api/chat"))
                {
                    // Custom rejection handling logic
                    context.HttpContext.Response.ContentType = "application/json";

                    var errorResponse = new
                    {
                        error = "Rate limit exceeded. Please wait before sending another message.",
                        retryAfter = 60,
                        requiresCaptcha = true,
                        statusCode = 429
                    };

                    await context.HttpContext.Response.WriteAsync(
                        System.Text.Json.JsonSerializer.Serialize(errorResponse),
                        cancellationToken);

                    // Optional logging
                    initialLogger.LogWarning("Rate limit exceeded for user: {User}, IP: {IpAddress}",
                            context.HttpContext.User.Identity?.Name ?? "anonymous",
                            context.HttpContext.Connection.RemoteIpAddress);
                    return;
                }

                await context.HttpContext.Response.WriteAsync("Rate limit exceeded. Please try again later.", cancellationToken);

                // Optional logging
                initialLogger.LogWarning("Rate limit exceeded for user: {User}, IP: {IpAddress}",
                        context.HttpContext.User.Identity?.Name ?? "anonymous",
                        context.HttpContext.Connection.RemoteIpAddress);
            };
        });

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
             })
             .AddGitHub(o =>
             {
                 o.ClientId = configuration["authentication:github:clientId"] ?? throw new InvalidOperationException("github:clientId unexpectedly null");
                 o.ClientSecret = configuration["authentication:github:clientSecret"] ?? throw new InvalidOperationException("github:clientSecret unexpectedly null");

                 // Grants access to read a user's profile data.
                 // https://docs.github.com/en/developers/apps/building-oauth-apps/scopes-for-oauth-apps
                 o.Scope.Add("read:user");
             });
        }

        loggerFactory.Dispose();

        WebApplication app = builder.Build();
        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error");
            app.UseForwardedHeaders();
            app.UseHsts();
            app.UseSecurityHeadersMiddleware(new SecurityHeadersBuilder()
                .AddDefaultSecurePolicy());
        }
        else
        {
            app.UseDeveloperExceptionPage();
            app.UseForwardedHeaders();
        }

        app.MapHealthChecks("/healthz");

        app.UseHttpsRedirection();
        app.UseStaticFiles();

        app.UseRouting();

        app.UseAuthentication();
        app.UseAuthorization();

        app.UseRateLimiter();

        app.UseMiddleware<ReferralMiddleware>();

        app.MapRazorPages();
        app.MapDefaultControllerRoute();

        app.MapFallbackToController("Index", "Home");

        // Generate sitemap.xml at startup
        var wwwrootDirectory = new DirectoryInfo(app.Environment.WebRootPath);
        var siteMappingService = app.Services.GetRequiredService<ISiteMappingService>();
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        // Extract base URL from configuration
        var baseUrl = configuration.GetSection("SiteSettings")["BaseUrl"] ?? "https://essentialcsharp.com";

        try
        {
            // Create a scope to resolve scoped services
            var routeConfigurationService = app.Services.GetRequiredService<IRouteConfigurationService>();

            SitemapXmlHelpers.EnsureSitemapHealthy(siteMappingService.SiteMappings.ToList());
            SitemapXmlHelpers.GenerateAndSerializeSitemapXml(wwwrootDirectory, siteMappingService.SiteMappings.ToList(), initialLogger, routeConfigurationService, baseUrl);
            logger.LogInformation("Sitemap.xml generation completed successfully during application startup");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to generate sitemap.xml during application startup");
            // Continue startup even if sitemap generation fails
        }

        app.Run();
    }
}
