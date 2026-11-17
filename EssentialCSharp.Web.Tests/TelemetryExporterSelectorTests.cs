using EssentialCSharp.Web.Services;
using Microsoft.OpenTelemetry;

namespace EssentialCSharp.Web.Tests;

public class TelemetryExporterSelectorTests
{
    private const string ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
    private const string OtlpEndpoint = "http://localhost:4317";

    [Test]
    [Arguments(ConnectionString, null, false, ExportTarget.AzureMonitor)]
    [Arguments(ConnectionString, null, true, ExportTarget.AzureMonitor)]
    [Arguments(ConnectionString, OtlpEndpoint, false, ExportTarget.AzureMonitor | ExportTarget.Otlp)]
    [Arguments(ConnectionString, OtlpEndpoint, true, ExportTarget.Otlp)]
    [Arguments(null, OtlpEndpoint, false, ExportTarget.Otlp)]
    [Arguments("", OtlpEndpoint, true, ExportTarget.Otlp)]
    [Arguments(" ", OtlpEndpoint, true, ExportTarget.Otlp)]
    [Arguments(ConnectionString, " ", true, ExportTarget.AzureMonitor)]
    [Arguments(null, null, false, ExportTarget.None)]
    [Arguments(" ", " ", true, ExportTarget.None)]
    public async Task Select_ChoosesExporters(string? connectionString, string? otlpEndpoint, bool isDevelopment, ExportTarget expected)
    {
        await Assert.That(TelemetryExporterSelector.Select(connectionString, otlpEndpoint, isDevelopment))
            .IsEqualTo(expected);
    }
}
