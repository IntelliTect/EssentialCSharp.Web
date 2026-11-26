using Microsoft.Extensions.Configuration;

namespace EssentialCSharp.Web.Tests;

/// <summary>
/// Guards the third-party hosts that analytics scripts need. A missing host fails silently in
/// production: the browser blocks the request and the tool simply records nothing.
/// The CSP header is only emitted outside Development, so these tests check the policy string directly.
/// </summary>
public class ContentSecurityPolicyTests
{
    [Test]
    // Clarity's tag on www.clarity.ms loads its tracker from scripts.clarity.ms (and other
    // [a-z].clarity.ms hosts). https://learn.microsoft.com/clarity/setup-and-installation/clarity-csp
    [Arguments("script-src", "https://*.clarity.ms")]
    [Arguments("script-src", "https://c.bing.com")]
    [Arguments("connect-src", "https://*.clarity.ms")]
    [Arguments("connect-src", "https://c.bing.com")]
    // Application Insights loads its SDK from, and fetches its CDN config from, js.monitor.azure.com.
    [Arguments("script-src", "js.monitor.azure.com")]
    [Arguments("connect-src", "https://js.monitor.azure.com")]
    [Arguments("script-src", "www.googletagmanager.com")]
    [Arguments("connect-src", "https://*.google-analytics.com")]
    public async Task ContentSecurityPolicy_AllowsAnalyticsHosts(string directive, string source)
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        string csp = Program.BuildContentSecurityPolicy(configuration);

        string[] sources = csp
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length > 0 && parts[0] == directive) ?? [];

        await Assert.That(sources).IsNotEmpty();
        await Assert.That(sources.Skip(1)).Contains(source);
    }
}
