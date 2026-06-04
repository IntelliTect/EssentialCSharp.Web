using System.Security.Claims;
using System.Runtime.InteropServices;
using System.Threading.RateLimiting;
using ModelContextProtocol.Protocol;
using EssentialCSharp.Chat.Common.Extensions;
using EssentialCSharp.Web.Areas.Identity.Data;
using EssentialCSharp.Web.Areas.Identity.Services.PasswordValidators;
using EssentialCSharp.Web.Auth;
using EssentialCSharp.Web.Data;
using EssentialCSharp.Web.Extensions;
using EssentialCSharp.Web.Helpers;
using EssentialCSharp.Web.Middleware;
using EssentialCSharp.Web.Services;
using EssentialCSharp.Web.Services.Referrals;
using EssentialCSharp.Web.Tools;
using Mailjet.Client;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.RateLimiting;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using Azure.Monitor.OpenTelemetry.Profiler;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace EssentialCSharp.Web;

public partial class Program
{
    private static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        // Health checks (liveness/readiness probes for ACA and standalone hosting)
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        // OpenTelemetry — two mutually exclusive export paths:
        //   Production:  Azure Monitor (Application Insights) via APPLICATIONINSIGHTS_CONNECTION_STRING
        //   Local/Aspire: OTLP to Aspire Dashboard via OTEL_EXPORTER_OTLP_ENDPOINT
        // Never both simultaneously — that would cause duplicate telemetry in App Insights.
        string? appInsightsConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        bool useAzureMonitor = !string.IsNullOrWhiteSpace(appInsightsConnectionString);
        bool useOtlp = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        bool profilerSupportedPlatform = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
        bool profilerSkippedUnsupportedPlatform = false;

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        // Health probe paths excluded from tracing unconditionally — applies to both
        // manual instrumentation and Azure Monitor's auto-instrumentation.
        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            options.Filter = ctx =>
                !ctx.Request.Path.StartsWithSegments("/health")
                && !ctx.Request.Path.StartsWithSegments("/alive");
            // EnrichWithHttpResponse fires after the authentication middleware has run,
            // so HttpContext.User is populated and IsAuthenticated is reliable.
            options.EnrichWithHttpResponse = (activity, response) =>
            {
                var user = response.HttpContext.User;
                if (user?.Identity?.IsAuthenticated != true)
                {
                    return;
                }

                string? userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!string.IsNullOrWhiteSpace(userId))
                {
                    activity.SetTag("enduser.id", userId);
                }
            };
        });

        var otel = builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                // Azure Monitor auto-instruments ASP.NET Core + HttpClient metrics; only add
                // them manually when using OTLP so we don't register duplicate meter listeners.
                if (!useAzureMonitor)
                {
                    metrics.AddAspNetCoreInstrumentation()
                           .AddHttpClientInstrumentation();
                }
                // Runtime metrics are not included in the Azure Monitor distro.
                metrics.AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName);
                // Azure Monitor distro auto-instruments tracing; add manually only for OTLP path.
                if (!useAzureMonitor)
                {
                    tracing.AddAspNetCoreInstrumentation()
                           .AddHttpClientInstrumentation()
                           .AddSqlClientInstrumentation();
                }
            });

        if (useAzureMonitor)
        {
            // Azure Monitor export is supported cross-platform, but the profiler currently only
            // supports Windows and Linux.
            var azureMonitor = otel.UseAzureMonitor();
            if (profilerSupportedPlatform)
                azureMonitor.AddAzureMonitorProfiler();
            else
                profilerSkippedUnsupportedPlatform = true;
        }
        else if (useOtlp)
            otel.UseOtlpExporter();

        // HttpClient defaults — standard retry/circuit breaker for all named clients.
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        builder.Services.AddHttpClient("HaveIBeenPwned", c =>
        {
            c.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EssentialCSharp.Web/1.0");
            // Short timeout: this check is advisory/fail-open, so cap latency impact on auth flows.
            c.Timeout = TimeSpan.FromSeconds(3);
        });

        builder.Services.AddTrustedForwardedHeaders(builder.Configuration, builder.Environment);


        ConfigurationManager configuration = builder.Configuration;
        string connectionString = builder.Configuration.GetConnectionString("EssentialCSharpWebContextConnection") ?? throw new InvalidOperationException("Connection string 'EssentialCSharpWebContextConnection' not found.");

        builder.Services.AddDbContext<EssentialCSharpWebContext>(options => options.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(5)));

        // Must be registered before AddDataProtection(): hosted services start in registration
        // order, and DataProtectionHostedService reads DataProtectionKeys during startup.
        builder.Services.AddHostedService<DatabaseMigrationService>();

        // Data Protection — persist keys to SQL Server so they survive container restarts.
        // SetApplicationName ensures the discriminator is stable across container hostname changes.
        var dpBuilder = builder.Services.AddDataProtection()
            .SetApplicationName("EssentialCSharpWeb")
            .PersistKeysToDbContext<EssentialCSharpWebContext>();
        var keyVaultKeyUri = builder.Configuration["DataProtection:AzureKeyVaultKeyUri"];
        if (!string.IsNullOrEmpty(keyVaultKeyUri))
        {
            dpBuilder.ProtectKeysWithAzureKeyVault(new Uri(keyVaultKeyUri), new Azure.Identity.DefaultAzureCredential());
        }
        else if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "DataProtection:AzureKeyVaultKeyUri is required in non-Development environments. " +
                "Set the DataProtection__AzureKeyVaultKeyUri environment variable to the Key Vault key URI.");
        }

        builder.Services.AddDefaultIdentity<EssentialCSharpWebUser>(options =>
        {
            // Password settings
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = PasswordRequirementOptions.PasswordMinimumLength;
            options.Password.RequireDigit = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
            options.Password.RequiredUniqueChars = 1;

            options.SignIn.RequireConfirmedEmail = true;
            options.SignIn.RequireConfirmedAccount = true;

            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(30);
            options.Lockout.MaxFailedAccessAttempts = 3;

            //TODO: Implement IProtectedUserStore
            //options.Stores.ProtectPersonalData = true;
        })
            .AddEntityFrameworkStores<EssentialCSharpWebContext>()
             .AddPasswordValidator<UsernameOrEmailAsPasswordValidator<EssentialCSharpWebUser>>()
             .AddPasswordValidator<Top100000PasswordValidator<EssentialCSharpWebUser>>()
             .AddPasswordValidator<PwnedPasswordValidator<EssentialCSharpWebUser>>();

        builder.Configuration
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets<Program>()
            .AddEnvironmentVariables();

        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(60);
            options.SlidingExpiration = true;
            // API endpoints must return 401/403 instead of redirecting to the login page.
            // Cookie auth's default behavior (302 redirect) causes fetch() to follow the
            // redirect, eventually hitting the fallback controller and returning a 404.
            options.Events.OnRedirectToLogin = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api")
                    || context.Request.Path.StartsWithSegments("/mcp"))
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                else
                    context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                if (context.Request.Path.StartsWithSegments("/api")
                    || context.Request.Path.StartsWithSegments("/mcp"))
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                else
                    context.Response.Redirect(context.RedirectUri);
                return Task.CompletedTask;
            };
        });

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

        builder.Services.AddControllersWithViews(options =>
        {
            options.Filters.Add(new Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute());
        });

        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddTransient<IEmailSender, EmailSender>();
        }
        builder.Services.Configure<AuthMessageSenderOptions>(builder.Configuration.GetSection(AuthMessageSenderOptions.AuthMessageSender));
        builder.Services.Configure<SiteSettings>(builder.Configuration.GetSection(SiteSettings.SectionName));

        // Add services to the container.
        builder.Services.AddRazorPages();
        builder.Services.AddOutputCache();
        builder.Services.AddCaptchaService(builder.Configuration.GetSection(CaptchaOptions.CaptchaSender));
        builder.Services.AddSingleton<ISiteMappingService, SiteMappingService>();
        builder.Services.AddSingleton<IRouteConfigurationService, RouteConfigurationService>();
        builder.Services.AddSingleton<IListingSourceCodeService, ListingSourceCodeService>();
        builder.Services.AddSingleton<IBookToolQueryService, BookToolQueryService>();
        builder.Services.AddScoped<IReferralService, ReferralService>();

        // Add AI Chat services using configuration-driven backend selection.
        builder.Services.AddConfiguredChatServices(configuration);

        // MCP server — always enabled, authenticated via opaque DB-backed tokens.
        builder.Services.AddScoped<McpApiTokenService>();

        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton(_ => new ResponseIdValidationService());

        builder.Services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, McpApiKeyAuthenticationHandler>(
                McpBearerAuthentication.Scheme, _ => { });

        builder.Services.AddAuthorization(options =>
            options.AddPolicy("McpPolicy", policy =>
                policy.AddAuthenticationSchemes(McpBearerAuthentication.Scheme)
                      .RequireAuthenticatedUser()));

        builder.Services.AddCors(options =>
            options.AddPolicy("McpInspectorCors", policy =>
                policy.SetIsOriginAllowed(origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out Uri? originUri)
                    && originUri.IsLoopback
                    && (originUri.Scheme == Uri.UriSchemeHttp || originUri.Scheme == Uri.UriSchemeHttps))
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders("Mcp-Session-Id")));

        builder.Services.AddSingleton<IGuidelinesService, GuidelinesService>();

        builder.Services.AddMcpServer()
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools<BookSearchTool>()
            .WithTools<BookListingTool>()
            .WithTools<BookGuidelinesTool>()
            .WithTools<BookContentTool>();

        // Add Rate Limiting for API endpoints
        builder.Services.AddRateLimiter(options =>
        {
            // Global rate limiter for site requests by authenticated user ID or anonymous IP.
            // MCP transport requests use a dedicated named policy attached to MapMcp("/mcp").
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
            {
                if (httpContext.Request.Path.StartsWithSegments("/.well-known"))
                    return RateLimitPartition.GetNoLimiter("well-known");

                if (IsMcpTransportRequest(httpContext.Request))
                    return RateLimitPartition.GetNoLimiter("mcp-transport");

                var partitionKey = httpContext.User.Identity?.IsAuthenticated == true
                    ? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-user"
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

            options.AddPolicy("ChatEndpoint", httpContext =>
            {
                // Partitioned per-user (when authenticated) or per-IP (anonymous)
                var partitionKey = httpContext.User.Identity?.IsAuthenticated == true
                    ? $"chat-user:{httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-user"}"
                    : $"chat-ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown-ip"}";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: partitionKey,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 15,
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = 0
                    });
            });

            // Combined per-minute burst (10/min) + per-hour cap (150/hr) for book content pages.
            // A scraper cycling through the full ~400-page book needs 2+ hours at minimum.
            // See Services/ContentRateLimiterPolicy.cs for implementation.
            options.AddPolicy<string>("content", new ContentRateLimiterPolicy());
            options.AddPolicy<string>(McpRateLimiterPolicy.PolicyName, new McpRateLimiterPolicy());

            // Custom response when rate limit is exceeded
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                int? retryAfterSeconds = RateLimitingResponseHelpers.ApplyRetryAfterHeader(
                    context.HttpContext.Response,
                    context.Lease);
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();

                if (context.HttpContext.Request.Path.StartsWithSegments("/api/chat"))
                {
                    // Custom rejection handling logic
                    context.HttpContext.Response.ContentType = "application/json";

                    Dictionary<string, object> errorResponse = new()
                    {
                        ["error"] = "Rate limit exceeded. Please wait before sending another message.",
                        ["statusCode"] = 429
                    };
                    if (retryAfterSeconds is int retryAfter)
                        errorResponse["retryAfter"] = retryAfter;

                    await context.HttpContext.Response.WriteAsync(
                        System.Text.Json.JsonSerializer.Serialize(errorResponse),
                        cancellationToken);

                    // Optional logging
                    LogRateLimitExceeded(
                        logger,
                        context.HttpContext.Request.Path,
                        context.HttpContext.User.Identity?.Name ?? "anonymous",
                        context.HttpContext.Connection.RemoteIpAddress);
                    return;
                }

                await context.HttpContext.Response.WriteAsync("Rate limit exceeded. Please try again later.", cancellationToken);

                LogRateLimitExceeded(
                    logger,
                    context.HttpContext.Request.Path,
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

        WebApplication app = builder.Build();

        if (profilerSkippedUnsupportedPlatform)
            LogSkippingUnsupportedAzureMonitorProfiler(
                app.Services.GetRequiredService<ILogger<Program>>(),
                RuntimeInformation.OSDescription);

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            SiteSettings siteSettings = app.Services.GetRequiredService<IOptions<SiteSettings>>().Value;
            if (!Uri.TryCreate(siteSettings.BaseUrl, UriKind.Absolute, out Uri? configuredBaseUri))
            {
                throw new InvalidOperationException($"Invalid {SiteSettings.SectionName}:{nameof(SiteSettings.BaseUrl)} value: '{siteSettings.BaseUrl}'.");
            }
            string apexHost = configuredBaseUri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? configuredBaseUri.Host[4..]
                : configuredBaseUri.Host;
            string wwwHost = $"www.{apexHost}";
            string redirectAuthority = new UriBuilder(configuredBaseUri) { Host = apexHost }.Uri.GetLeftPart(UriPartial.Authority);

            app.UseExceptionHandler(exceptionApp =>
            {
                exceptionApp.Run(async context =>
                {
                    var exceptionFeature = context.Features.Get<IExceptionHandlerFeature>();
                    var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                    LogUnhandledException(logger, exceptionFeature?.Error, context.Request.Path);

                    if (context.Request.Path.StartsWithSegments("/mcp"))
                    {
                        await McpJsonRpcResponseWriter.WriteErrorAsync(
                            context.Response,
                            StatusCodes.Status500InternalServerError,
                            -32603,
                            "An unexpected error occurred while processing the MCP request.",
                            context.RequestAborted);
                    }
                    else if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = 500;
                        context.Response.ContentType = "application/json";
                        await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred" });
                    }
                    else
                    {
                        context.Response.Redirect("/Home/Error?statusCode=500");
                    }
                });
            });
            // Skip manual UseForwardedHeaders when ASPNETCORE_FORWARDEDHEADERS_ENABLED=true;
            // the built-in startup filter already called it before this pipeline runs.
            if (!string.Equals(app.Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            {
                app.UseForwardedHeaders();
            }

            // Build dynamic CSP — TryDotNet origin comes from runtime config
            string? tryDotNetOrigin = app.Configuration["TryDotNet:Origin"];
            string tryDotNetSources = string.Empty;
            if (!string.IsNullOrWhiteSpace(tryDotNetOrigin))
            {
                if (Uri.TryCreate(tryDotNetOrigin, UriKind.Absolute, out Uri? tryDotNetUri))
                {
                    tryDotNetSources = $" {tryDotNetUri.GetLeftPart(UriPartial.Authority)}";
                }
                else
                {
                    LogIgnoringInvalidTryDotNetOrigin(app.Logger, tryDotNetOrigin);
                }
            }

            string csp = string.Join("; ",
                $"default-src 'self'",
                $"script-src 'self' 'unsafe-inline' cdn.jsdelivr.net www.clarity.ms www.googletagmanager.com js.monitor.azure.com https://hcaptcha.com https://*.hcaptcha.com{tryDotNetSources}",
                $"style-src 'self' 'unsafe-inline' cdnjs.cloudflare.com fonts.googleapis.com https://hcaptcha.com https://*.hcaptcha.com",
                $"font-src 'self' fonts.gstatic.com cdnjs.cloudflare.com",
                $"img-src 'self' data: https:",
                $"connect-src 'self' https://hcaptcha.com https://*.hcaptcha.com https://api.pwnedpasswords.com https://*.algolia.net https://*.algolianet.com https://*.google-analytics.com https://*.clarity.ms https://*.in.applicationinsights.azure.com{GetApplicationInsightsCspSources(app.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"], app.Logger)}{tryDotNetSources}",
                $"frame-src https://hcaptcha.com https://*.hcaptcha.com https://newassets.hcaptcha.com{tryDotNetSources}",
                $"worker-src blob:",
                $"frame-ancestors 'none'",
                $"base-uri 'self'",
                $"form-action 'self' https://login.microsoftonline.com https://github.com"
            );

            app.UseSecurityHeadersMiddleware(new SecurityHeadersBuilder()
                .AddDefaultSecurePolicy()
                .AddContentSecurityPolicy(csp));

            // Redirect configured www host to configured apex host (permanent 301).
            // Must be after UseForwardedHeaders so the Host header reflects the real hostname.
            app.Use(async (context, next) =>
            {
                if (string.Equals(context.Request.Host.Host, wwwHost, StringComparison.OrdinalIgnoreCase))
                {
                    string redirectUrl = $"{redirectAuthority}{context.Request.PathBase}{context.Request.Path}{context.Request.QueryString}";
                    context.Response.Redirect(redirectUrl, permanent: true);
                    return;
                }
                await next(context);
            });
        }
        else
        {
            app.UseDeveloperExceptionPage();
            if (!string.Equals(app.Configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            {
                app.UseForwardedHeaders();
            }
        }

        app.MapHealthChecks("/health").DisableRateLimiting();
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        }).DisableRateLimiting();

        if (app.Environment.IsDevelopment())
        {
        app.UseHttpsRedirection();
        }
        app.UseStaticFiles();

        app.UseRouting();

        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/mcp"),
            branch => branch.UseCors("McpInspectorCors"));

        app.UseAuthentication();

        app.UseWhen(
            context => context.Request.Path.StartsWithSegments("/mcp"),
            branch => branch.Use(async (context, next) =>
            {
                // /mcp uses a named non-default scheme. Normalize the principal before
                // rate limiting so valid MCP requests partition by MCP user while
                // missing/invalid bearer requests fall back to the anonymous/IP bucket
                // instead of inheriting the site's cookie principal.
                McpApiTokenService.ResolvedMcpApiToken? resolvedToken = null;
                if (McpBearerAuthentication.TryGetRawToken(context.Request, out string? rawToken))
                {
                    var tokenService = context.RequestServices.GetRequiredService<McpApiTokenService>();
                    resolvedToken = await tokenService.ResolveValidTokenAsync(rawToken, context.RequestAborted);
                    McpBearerAuthentication.StoreResolution(context, resolvedToken);
                }

                context.User = resolvedToken is not null
                    ? McpBearerAuthentication.CreatePrincipal(resolvedToken.UserId)
                    : new ClaimsPrincipal(new ClaimsIdentity());

                await next(context);
            }));

        app.UseRateLimiter();

        app.UseAuthorization();
        app.UseOutputCache();

        app.UseMiddleware<ReferralMiddleware>();

        app.MapRazorPages();
        app.MapDefaultControllerRoute();

        app.MapMethods("/mcp", [HttpMethods.Get], (HttpResponse response) =>
        {
            response.Headers.Append("Allow", HttpMethods.Post);
            response.Headers.CacheControl = "no-store";
            return Results.StatusCode(StatusCodes.Status405MethodNotAllowed);
        });

        app.MapMcp("/mcp")
            .RequireAuthorization("McpPolicy")
            .RequireRateLimiting(McpRateLimiterPolicy.PolicyName);

        app.Map("/.well-known", (HttpResponse response) =>
        {
            response.Headers.CacheControl = "no-store";
            return Results.NotFound();
        }).DisableRateLimiting();

        app.Map("/.well-known/{**path}", (HttpResponse response) =>
        {
            response.Headers.CacheControl = "no-store";
            return Results.NotFound();
        }).DisableRateLimiting();

        app.MapFallbackToController("Index", "Home");

        // Validate sitemap data at startup — logs errors but allows startup to continue
        var siteMappingService = app.Services.GetRequiredService<ISiteMappingService>();
        var logger = app.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            SitemapXmlHelpers.EnsureSitemapHealthy(siteMappingService.SiteMappings.ToList());
            LogSitemapValidationSucceeded(logger);
        }
        catch (InvalidOperationException ex)
        {
            LogSitemapValidationFailed(logger, ex);
            // Continue startup even if sitemap validation fails
        }

        app.Run();
    }

    private static bool IsMcpTransportRequest(HttpRequest request) =>
        HttpMethods.IsPost(request.Method) && request.Path == "/mcp";

    [LoggerMessage(Level = LogLevel.Warning, Message = "Rate limit exceeded on {Path}. User: {User}, IP: {IpAddress}")]
    private static partial void LogRateLimitExceeded(ILogger<Program> logger, PathString path, string user, System.Net.IPAddress? ipAddress);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception on {Path}")]
    private static partial void LogUnhandledException(ILogger<Program> logger, Exception? exception, PathString path);

    [LoggerMessage(Level = LogLevel.Information, Message = "Sitemap validation completed successfully during application startup")]
    private static partial void LogSitemapValidationSucceeded(ILogger<Program> logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to validate sitemap during application startup")]
    private static partial void LogSitemapValidationFailed(ILogger<Program> logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ignoring invalid TryDotNet origin in CSP: {Origin}")]
    private static partial void LogIgnoringInvalidTryDotNetOrigin(ILogger logger, string origin);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Azure Monitor profiler is not supported on this platform ({Platform}). Skipping profiler registration and continuing with Azure Monitor telemetry export.")]
    private static partial void LogSkippingUnsupportedAzureMonitorProfiler(ILogger<Program> logger, string platform);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Application Insights connection string has a non-HTTPS or unparseable IngestionEndpoint value ({Endpoint}); omitting from CSP connect-src.")]
    private static partial void LogInvalidApplicationInsightsIngestionEndpoint(ILogger logger, string? endpoint);

    private static string GetApplicationInsightsCspSources(string? connectionString, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        string? ingestionEndpoint = GetConnectionStringValue(connectionString, "IngestionEndpoint");
        if (string.IsNullOrWhiteSpace(ingestionEndpoint)
            || !Uri.TryCreate(ingestionEndpoint, UriKind.Absolute, out Uri? ingestionUri)
            || ingestionUri.Scheme != Uri.UriSchemeHttps)
        {
            if (logger is not null)
            {
                LogInvalidApplicationInsightsIngestionEndpoint(logger, ingestionEndpoint);
            }
            return string.Empty;
        }

        return $" {ingestionUri.GetLeftPart(UriPartial.Authority)}";
    }

    private static string? GetConnectionStringValue(string connectionString, string key)
    {
        foreach (string segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separatorIndex = segment.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string currentKey = segment[..separatorIndex];
            if (!currentKey.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return segment[(separatorIndex + 1)..].Trim('"');
        }

        return null;
    }
}
