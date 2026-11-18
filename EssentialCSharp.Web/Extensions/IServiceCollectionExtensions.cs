using System.Net;
using EssentialCSharp.Web.Services;
using Mailjet.Client;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace EssentialCSharp.Web.Extensions;

public static class IServiceCollectionExtensions
{
    /// <summary>
    /// Upper bound for a single Mailjet request. The send runs synchronously inside the registration and
    /// password-reset requests, so a slow Mailjet must not hold them open for HttpClient's 100s default.
    /// Matches the 10s per-attempt default of the standard resilience handler.
    /// </summary>
    internal static readonly TimeSpan MailjetRequestTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Standard retry/circuit-breaker resilience for every HttpClient. Clients where retrying is unsafe
    /// (for example the Mailjet send) opt out with <c>RemoveAllResilienceHandlers</c>.
    /// </summary>
    public static IServiceCollection AddGlobalHttpClientDefaults(this IServiceCollection services)
    {
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        return services;
    }

    /// <summary>
    /// Registers the Mailjet-backed <see cref="IEmailSender"/> with validated <see cref="AuthMessageSenderOptions"/>.
    /// Does nothing in Development, where emails are not sent and no Mailjet keys are needed. Elsewhere,
    /// missing or malformed settings fail host startup (<c>ValidateOnStart</c>).
    /// </summary>
    public static IServiceCollection AddMailjetEmailSender(this IServiceCollection services, IConfigurationSection authMessageSenderSection, IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            return services;
        }

        services.AddOptions<AuthMessageSenderOptions>()
            .Bind(authMessageSenderSection)
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AuthMessageSenderOptions>, AuthMessageSenderOptionsValidator>());
        services.AddTransient<IEmailSender, EmailSender>();

        // RemoveAllResilienceHandlers is an experimental API (EXTEXP0001); MailjetEmailSenderPipelineTests
        // (SendEmailAsync_WhenMailjetReturnsServerError_...) guards its behavior when the resilience package is bumped.
#pragma warning disable EXTEXP0001
        services.AddHttpClient<IMailjetClient, MailjetClient>((serviceProvider, client) =>
        {
            AuthMessageSenderOptions options = serviceProvider.GetRequiredService<IOptions<AuthMessageSenderOptions>>().Value;

            //set BaseAddress, MediaType, UserAgent
            client.SetDefaultSettings();
            client.UseBasicAuthentication(options.APIKey, options.SecretKey);
            client.Timeout = MailjetRequestTimeout;
        })
        // Remove the global standard resilience handler (ConfigureHttpClientDefaults in Program.cs): its retries
        // would re-POST a send that may already have been accepted and could deliver duplicate emails.
        // HttpClient.Timeout above bounds the single attempt instead.
        .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        return services;
    }

    public static void AddCaptchaService(this IServiceCollection services, IConfigurationSection CaptchaOptions)
    {
        services.AddOptions<CaptchaOptions>()
            .Bind(CaptchaOptions)
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CaptchaOptions>, CaptchaOptionsValidator>();
        services.AddSingleton<ICaptchaService, CaptchaService>();
        services.AddSingleton<ICaptchaValidationService, CaptchaValidationService>();
        services.AddHttpClient("hCaptcha", (serviceProvider, client) =>
        {
            client.BaseAddress = new Uri(serviceProvider.GetRequiredService<IOptions<CaptchaOptions>>().Value.ApiBaseUrl);
        });
    }

    public static void AddTrustedForwardedHeaders(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // When ASPNETCORE_FORWARDEDHEADERS_ENABLED=true (recommended for Azure Container Apps),
        // ASP.NET Core's built-in startup filter handles ForwardedHeaders with all proxies trusted.
        // Skip manual configuration to avoid a conflicting no-trusted-proxies throw on startup.
        if (string.Equals(configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            return;

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;

            var trustedProxyCidrs = configuration
                .GetSection("ForwardedHeaders:TrustedProxyCidrs")
                .Get<string[]>() ?? [];
            var trustedProxies = configuration
                .GetSection("ForwardedHeaders:TrustedProxies")
                .Get<string[]>() ?? [];

            if (trustedProxyCidrs.Length == 0 && trustedProxies.Length == 0)
            {
                if (!environment.IsDevelopment())
                {
                    throw new InvalidOperationException(
                        "Forwarded headers are enabled but no trusted proxies are configured. " +
                        "Set ForwardedHeaders:TrustedProxyCidrs or ForwardedHeaders:TrustedProxies, " +
                        "or set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true for platform-managed proxies (e.g. Azure Container Apps).");
                }
                return;
            }

            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            foreach (var cidr in trustedProxyCidrs)
            {
                if (string.IsNullOrWhiteSpace(cidr) || !System.Net.IPNetwork.TryParse(cidr.Trim(), out var network))
                    throw new InvalidOperationException($"Invalid ForwardedHeaders:TrustedProxyCidrs entry '{cidr}'. Use CIDR notation, e.g. '10.0.0.0/8'.");

                options.KnownIPNetworks.Add(network);
            }

            foreach (var proxy in trustedProxies)
            {
                if (!IPAddress.TryParse(proxy, out var proxyAddress))
                    throw new InvalidOperationException($"Invalid ForwardedHeaders:TrustedProxies entry '{proxy}'. Use a valid IP address.");

                options.KnownProxies.Add(proxyAddress);
            }
        });
    }
}
