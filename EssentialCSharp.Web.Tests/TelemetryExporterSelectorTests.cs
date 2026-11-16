using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

public class TelemetryExporterSelectorTests
{
    private const string ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
    private const string OtlpEndpoint = "http://localhost:4317";

    [Test]
    [Arguments(ConnectionString, null, false, TelemetryExporter.AzureMonitor)]
    [Arguments(ConnectionString, null, true, TelemetryExporter.AzureMonitor)]
    [Arguments(ConnectionString, OtlpEndpoint, false, TelemetryExporter.AzureMonitor)]
    [Arguments(ConnectionString, OtlpEndpoint, true, TelemetryExporter.Otlp)]
    [Arguments(null, OtlpEndpoint, false, TelemetryExporter.Otlp)]
    [Arguments("", OtlpEndpoint, true, TelemetryExporter.Otlp)]
    [Arguments(null, null, false, TelemetryExporter.None)]
    [Arguments(" ", " ", true, TelemetryExporter.None)]
    public async Task Select_ChoosesExporter(string? connectionString, string? otlpEndpoint, bool isDevelopment, TelemetryExporter expected)
    {
        await Assert.That(TelemetryExporterSelector.Select(connectionString, otlpEndpoint, isDevelopment)).IsEqualTo(expected);
    }
}
