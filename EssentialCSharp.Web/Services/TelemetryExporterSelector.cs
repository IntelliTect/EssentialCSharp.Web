using Microsoft.OpenTelemetry;

namespace EssentialCSharp.Web.Services;

public static class TelemetryExporterSelector
{
    /// <summary>
    /// Deployed environments export to both: Application Insights and the OTLP endpoint that Azure Container Apps'
    /// Aspire Dashboard injects. Locally (Development) an OTLP endpoint means Aspire, so a developer's App Insights
    /// connection string doesn't send their local telemetry to Azure.
    /// </summary>
    public static ExportTarget Select(string? appInsightsConnectionString, string? otlpEndpoint, bool isDevelopment)
    {
        bool hasAppInsights = !string.IsNullOrWhiteSpace(appInsightsConnectionString);
        bool hasOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

        return (hasAppInsights && !(hasOtlp && isDevelopment) ? ExportTarget.AzureMonitor : ExportTarget.None)
            | (hasOtlp ? ExportTarget.Otlp : ExportTarget.None);
    }
}
