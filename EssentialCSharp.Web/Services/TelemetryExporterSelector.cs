using Microsoft.OpenTelemetry;

namespace EssentialCSharp.Web.Services;

public static class TelemetryExporterSelector
{
    /// <summary>
    /// Selects configured destinations; Development with OTLP remains dashboard-only.
    /// </summary>
    public static ExportTarget Select(string? appInsightsConnectionString, string? otlpEndpoint, bool isDevelopment)
    {
        bool hasAppInsights = !string.IsNullOrWhiteSpace(appInsightsConnectionString);
        bool hasOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

        return (hasAppInsights && !(hasOtlp && isDevelopment) ? ExportTarget.AzureMonitor : ExportTarget.None)
            | (hasOtlp ? ExportTarget.Otlp : ExportTarget.None);
    }
}
