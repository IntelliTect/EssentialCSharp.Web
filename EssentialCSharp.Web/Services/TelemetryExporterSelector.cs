namespace EssentialCSharp.Web.Services;

public enum TelemetryExporter
{
    None,
    AzureMonitor,
    Otlp,
}

public static class TelemetryExporterSelector
{
    /// <summary>
    /// An ambient OTLP endpoint only displaces Azure Monitor in Development (Aspire), so a deployed
    /// environment that injects one cannot silently disable Application Insights.
    /// </summary>
    public static TelemetryExporter Select(string? appInsightsConnectionString, string? otlpEndpoint, bool isDevelopment)
    {
        bool hasAppInsights = !string.IsNullOrWhiteSpace(appInsightsConnectionString);
        bool hasOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

        if (hasAppInsights && !(hasOtlp && isDevelopment))
        {
            return TelemetryExporter.AzureMonitor;
        }

        return hasOtlp ? TelemetryExporter.Otlp : TelemetryExporter.None;
    }
}
