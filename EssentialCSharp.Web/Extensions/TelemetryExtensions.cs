using System.Security.Claims;
using Azure.Monitor.OpenTelemetry.Profiler;
using EssentialCSharp.Web.Services;
using Microsoft.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace EssentialCSharp.Web.Extensions;

internal static class TelemetryExtensions
{
    /// <returns>True when the selected Azure profiler is unsupported on this OS.</returns>
    internal static bool ConfigureApplicationTelemetry(this IHostApplicationBuilder builder)
    {
        string? connectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"];
        ExportTarget exporters = TelemetryExporterSelector.Select(
            connectionString,
            builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"],
            builder.Environment.IsDevelopment());
        bool useAzureMonitor = exporters.HasFlag(ExportTarget.AzureMonitor);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.Configure<AspNetCoreTraceInstrumentationOptions>(options =>
        {
            options.Filter = ctx =>
                !ctx.Request.Path.StartsWithSegments("/health")
                && !ctx.Request.Path.StartsWithSegments("/alive")
                && !ctx.Request.Path.StartsWithSegments("/version");
            // Response enrichment runs after authentication has populated HttpContext.User.
            options.EnrichWithHttpResponse = (activity, response) =>
            {
                var user = response.HttpContext.User;
                if (user.Identity?.IsAuthenticated == true
                    && user.FindFirstValue(ClaimTypes.NameIdentifier) is string userId
                    && !string.IsNullOrWhiteSpace(userId))
                {
                    activity.SetTag("enduser.id", userId);
                }
            };
        });

        var otel = builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing.AddSource(builder.Environment.ApplicationName));

        if (useAzureMonitor)
        {
            otel.UseMicrosoftOpenTelemetry(options =>
            {
                options.Exporters = exporters;
                options.AzureMonitor.ConnectionString = connectionString;
                // Avoid enabling additional AI/agent telemetry during migration.
                options.Instrumentation.EnableOpenAIInstrumentation = false;
                options.Instrumentation.EnableSemanticKernelInstrumentation = false;
                options.Instrumentation.EnableAgentFrameworkInstrumentation = false;
                options.Instrumentation.EnableAgent365Instrumentation = false;
            });

            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                otel.AddAzureMonitorProfiler();
                return false;
            }

            return true;
        }

        // The distro starts Azure SDK statistics even in OTLP-only mode; keep local Aspire on the standard SDK.
        otel.WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation())
            .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation());
        if (exporters.HasFlag(ExportTarget.Otlp))
        {
            otel.UseOtlpExporter();
        }

        return false;
    }
}
