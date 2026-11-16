using EssentialCSharp.Web.Services;

namespace EssentialCSharp.Web.Tests;

public class TelemetryExporterSelectorTests
{
    private const string ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
    private const string OtlpEndpoint = "http://localhost:4317";

    [Test]
    [Arguments(ConnectionString, null, false, true, false)]
    [Arguments(ConnectionString, null, true, true, false)]
    [Arguments(ConnectionString, OtlpEndpoint, false, true, true)] // Deployed with the Aspire Dashboard component.
    [Arguments(ConnectionString, OtlpEndpoint, true, false, true)] // Local Aspire run.
    [Arguments(null, OtlpEndpoint, false, false, true)]
    [Arguments("", OtlpEndpoint, true, false, true)]
    [Arguments(null, null, false, false, false)]
    [Arguments(" ", " ", true, false, false)]
    public async Task Select_ChoosesExporters(string? connectionString, string? otlpEndpoint, bool isDevelopment, bool azureMonitor, bool otlp)
    {
        await Assert.That(TelemetryExporterSelector.Select(connectionString, otlpEndpoint, isDevelopment))
            .IsEqualTo(new TelemetryExporters(azureMonitor, otlp));
    }
}
