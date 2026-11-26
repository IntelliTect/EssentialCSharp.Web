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
        string csp = Program.BuildContentSecurityPolicy(new ConfigurationBuilder().Build());

        await Assert.That(GetSources(csp, directive)).Contains(source);
    }

    [Test]
    // Configured origins are reduced to scheme://host[:port] and added to the directives that need them.
    [Arguments("script-src", "https://try.example.test")]
    [Arguments("connect-src", "https://try.example.test")]
    [Arguments("frame-src", "https://try.example.test")]
    [Arguments("connect-src", "https://westus3-1.in.applicationinsights.azure.com")]
    public async Task ContentSecurityPolicy_AddsConfiguredOrigins(string directive, string source)
    {
        string csp = Program.BuildContentSecurityPolicy(ConfigurationWith(
            ("TryDotNet:Origin", "https://try.example.test/some/path"),
            ("APPLICATIONINSIGHTS_CONNECTION_STRING",
                "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://westus3-1.in.applicationinsights.azure.com/")));

        await Assert.That(GetSources(csp, directive)).Contains(source);
    }

    [Test]
    public async Task ContentSecurityPolicy_IgnoresInvalidOrigins()
    {
        string withInvalid = Program.BuildContentSecurityPolicy(ConfigurationWith(
            ("TryDotNet:Origin", "not a url"),
            ("APPLICATIONINSIGHTS_CONNECTION_STRING", "InstrumentationKey=x;IngestionEndpoint=http://insecure.example.test/")));
        string withNone = Program.BuildContentSecurityPolicy(new ConfigurationBuilder().Build());

        await Assert.That(withInvalid).IsEqualTo(withNone);
    }

    [Test]
    public async Task ContentSecurityPolicy_IsWellFormed()
    {
        string csp = Program.BuildContentSecurityPolicy(ConfigurationWith(("TryDotNet:Origin", "https://try.example.test")));

        await Assert.That(csp).DoesNotContain("  ");
        await Assert.That(csp).DoesNotContain(" ;");
        await Assert.That(csp.Split("; ").Select(d => d.Split(' ')[0])).IsEquivalentTo(
        [
            "default-src", "script-src", "style-src", "style-src-attr", "font-src", "img-src",
            "connect-src", "frame-src", "worker-src", "frame-ancestors", "base-uri", "form-action",
        ]);
    }

    private static IConfiguration ConfigurationWith(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static string[] GetSources(string csp, string directive) =>
        csp.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length > 0 && parts[0] == directive)?
            .Skip(1).ToArray() ?? [];
}
