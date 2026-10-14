using System.Net;
using EssentialCSharp.Web.Services;
using Microsoft.AspNetCore.HttpOverrides;

namespace EssentialCSharp.Web.Extensions;

public static class IServiceCollectionExtensions
{
    public static void AddCaptchaService(this IServiceCollection services, IConfigurationSection CaptchaOptions)
    {
        services.Configure<CaptchaOptions>(CaptchaOptions);
        services.AddSingleton<ICaptchaService, CaptchaService>();
        services.AddSingleton<ICaptchaValidationService, CaptchaValidationService>();
        services.AddHttpClient("hCaptcha", c =>
        {
            c.BaseAddress = new Uri("https://api.hcaptcha.com");
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
