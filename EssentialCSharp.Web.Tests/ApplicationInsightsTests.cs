using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Extensions.DependencyInjection;

namespace EssentialCSharp.Web.Tests;

public class ApplicationInsightsTests
{
    [Fact]
    public void WhenTheApplicationStarts_ApplicationInsightsIsRegistered()
    {
        using WebApplicationFactory factory = new();

        // Verify that Application Insights services are registered
        var services = factory.Services;
        
        // Check if TelemetryConfiguration is registered (core Application Insights service)
        var telemetryConfiguration = services.GetService<TelemetryConfiguration>();
        Assert.NotNull(telemetryConfiguration);
    }

    [Fact]
    public async Task WhenTheApplicationStarts_HealthCheckIsAvailable()
    {
        using WebApplicationFactory factory = new();

        HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/healthz");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }
}