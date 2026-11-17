using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using Azure.Monitor.OpenTelemetry.Exporter;
using Azure.Monitor.OpenTelemetry.Profiler.Core;
using EssentialCSharp.Web.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.OpenTelemetry;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Instrumentation.AspNetCore;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace EssentialCSharp.Web.Tests;

[NotInParallel]
public class TelemetryRegistrationTests
{
    private const string ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000";
    private const string OtlpEndpoint = "http://localhost:4317";
    private static readonly Action<ILogger, Exception?> s_logException =
        LoggerMessage.Define(LogLevel.Error, new EventId(1), "Telemetry regression");

    [Test]
    [Arguments("Development", ConnectionString, OtlpEndpoint, false)]
    [Arguments("Production", null, OtlpEndpoint, false)]
    [Arguments("Development", null, null, false)]
    [Arguments("Production", ConnectionString, OtlpEndpoint, true)]
    [Arguments("Development", ConnectionString, null, true)]
    public async Task Configure_RegistersAzureServicesOnlyWhenSelected(
        string environment, string? connectionString, string? otlpEndpoint, bool azureMonitor)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            ApplicationName = typeof(Program).Assembly.GetName().Name,
        });
        AddSettings(builder.Configuration, connectionString, otlpEndpoint);

        bool profilerSkipped = builder.ConfigureApplicationTelemetry();

        await Assert.That(builder.Services.Any(d => d.ServiceType == typeof(IConfigureOptions<AzureMonitorExporterOptions>)))
            .IsEqualTo(azureMonitor);
        bool profilerSupported = OperatingSystem.IsWindows() || OperatingSystem.IsLinux();
        await Assert.That(builder.Services.Any(d => d.ServiceType == typeof(IConfigureOptions<ServiceProfilerOptions>)))
            .IsEqualTo(azureMonitor && profilerSupported);
        await Assert.That(profilerSkipped).IsEqualTo(azureMonitor && !profilerSupported);
        if (!azureMonitor)
        {
            // Catch distro initialization itself, not just disabled Azure application exporters:
            // OTLP-only distro initialization also starts process-wide Azure SDK statistics.
            await Assert.That(builder.Services.Any(d =>
                d.ServiceType.Assembly == typeof(MicrosoftOpenTelemetryOptions).Assembly
                || d.ImplementationInstance?.GetType().Assembly == typeof(MicrosoftOpenTelemetryOptions).Assembly
                || d.ImplementationType?.Assembly == typeof(MicrosoftOpenTelemetryOptions).Assembly)).IsFalse();
        }

        // Resolve options only, without starting Azure providers or the profiler.
        using ServiceProvider services = builder.Services.BuildServiceProvider();
        OpenTelemetryLoggerOptions logging = services.GetRequiredService<IOptions<OpenTelemetryLoggerOptions>>().Value;
        await Assert.That(logging.IncludeFormattedMessage).IsTrue();
        await Assert.That(logging.IncludeScopes).IsTrue();
        if (azureMonitor)
        {
            AzureMonitorExporterOptions azure = services.GetRequiredService<IOptions<AzureMonitorExporterOptions>>().Value;
            await Assert.That(azure.ConnectionString).IsEqualTo(connectionString);
            await Assert.That(azure.EnableLiveMetrics).IsTrue();
            await Assert.That(azure.TracesPerSecond).IsEqualTo(5.0);
            if (profilerSupported)
            {
                await Assert.That(services.GetRequiredService<IOptions<ServiceProfilerOptions>>().Value).IsNotNull();
            }
        }
    }

    [Test]
    [Arguments("Development", OtlpEndpoint)]
    [Arguments("Production", OtlpEndpoint)]
    public async Task Configure_PreservesProbeFilteringAndResponseEnrichment(string environment, string otlpEndpoint)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            ApplicationName = typeof(Program).Assembly.GetName().Name,
        });
        AddSettings(builder.Configuration, ConnectionString, otlpEndpoint);
        builder.ConfigureApplicationTelemetry();
        using ServiceProvider services = builder.Services.BuildServiceProvider();
        var options = services.GetRequiredService<IOptions<AspNetCoreTraceInstrumentationOptions>>().Value;

        foreach (string path in new[] { "/health", "/health/ready", "/alive", "/version" })
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            await Assert.That(options.Filter!(context)).IsFalse();
        }
        foreach (string path in new[] { "/", "/healthcare", "/aliveness", "/versioned" })
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            await Assert.That(options.Filter!(context)).IsTrue();
        }

        foreach ((string? authenticationType, string? userId, string? expected) in new[]
        {
            ("test", "user-123", "user-123"),
            ((string?)null, "user-123", null),
            ("test", (string?)null, null),
            ("test", " ", null),
        })
        {
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    userId is null ? [] : [new Claim(ClaimTypes.NameIdentifier, userId)], authenticationType)),
            };
            using var activity = new Activity("response");
            options.EnrichWithHttpResponse!(activity, context.Response);
            await Assert.That(activity.GetTagItem("enduser.id")).IsEqualTo(expected);
        }
    }

    [Test]
    public async Task LocalAspire_WithAppInsightsConfigured_ExportsAllSignalsAndPreservesRequestTelemetry()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
            ApplicationName = typeof(Program).Assembly.GetName().Name,
        });
        builder.WebHost.UseTestServer();
        AddSettings(builder.Configuration, ConnectionString, OtlpEndpoint);
        builder.ConfigureApplicationTelemetry();

        using var otlp = new RecordingOtlpHandler();
        builder.Services.ConfigureAll<OtlpExporterOptions>(options =>
        {
            options.Protocol = OtlpExportProtocol.HttpProtobuf;
            options.HttpClientFactory = () => new HttpClient(otlp, disposeHandler: false);
        });
        var activities = new ConcurrentQueue<Activity>();
        var runtimeMeters = new ConcurrentQueue<string>();
        var exceptions = new ConcurrentQueue<Exception>();
        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddProcessor(
                new SimpleActivityExportProcessor(new RecordingExporter<Activity>(activities.Enqueue))))
            .WithMetrics(metrics => metrics.AddReader(new PeriodicExportingMetricReader(
                new RecordingExporter<Metric>(metric => runtimeMeters.Enqueue(metric.MeterName)),
                exportIntervalMilliseconds: int.MaxValue)))
            .WithLogging(logging => logging.AddProcessor(new SimpleLogRecordExportProcessor(
                new RecordingExporter<LogRecord>(record =>
                {
                    if (record.Exception is not null)
                        exceptions.Enqueue(record.Exception);
                }))));

        await using WebApplication app = builder.Build();
        using var source = new ActivitySource(builder.Environment.ApplicationName);
        var exception = new InvalidOperationException("telemetry regression");
        app.MapGet("/{**path}", (HttpContext context, ILogger<TelemetryRegistrationTests> logger) =>
        {
            Activity.Current?.SetTag("test.request.path", context.Request.Path.Value);
            if (context.Request.Path == "/authenticated")
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, "user-123")], "test"));
                using Activity? activity = source.StartActivity("business-operation");
                s_logException(logger, exception);
            }
            return Results.NoContent();
        });
        await app.StartAsync();
        using HttpClient client = app.GetTestClient();
        foreach (string path in new[] { "/authenticated", "/anonymous", "/health", "/health/ready", "/alive", "/version", "/healthcare" })
        {
            using HttpResponseMessage response = await client.GetAsync(path);
            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }

        var traces = app.Services.GetRequiredService<TracerProvider>();
        var metrics = app.Services.GetRequiredService<MeterProvider>();
        var logs = app.Services.GetRequiredService<LoggerProvider>();
        await Assert.That(traces.ForceFlush()).IsTrue();
        await Assert.That(metrics.ForceFlush()).IsTrue();
        await Assert.That(logs.ForceFlush()).IsTrue();

        Activity[] requests = activities.Where(a => a.Kind == ActivityKind.Server).ToArray();
        string?[] expectedPaths = ["/authenticated", "/anonymous", "/healthcare"];
        await Assert.That(requests.Select(a => a.GetTagItem("test.request.path") as string))
            .IsEquivalentTo(expectedPaths);
        await Assert.That(requests.Single(a => Equals(a.GetTagItem("test.request.path"), "/authenticated"))
            .GetTagItem("enduser.id")).IsEqualTo("user-123");
        await Assert.That(requests.Single(a => Equals(a.GetTagItem("test.request.path"), "/anonymous"))
            .GetTagItem("enduser.id")).IsNull();
        await Assert.That(activities.Count(a => a.OperationName == "business-operation")).IsEqualTo(1);
        await Assert.That(exceptions.Contains(exception)).IsTrue();
        await Assert.That(runtimeMeters.Contains("System.Runtime")).IsTrue();
        await Assert.That(otlp.Paths.Contains("/v1/traces")).IsTrue();
        await Assert.That(otlp.Paths.Contains("/v1/metrics")).IsTrue();
        await Assert.That(otlp.Paths.Contains("/v1/logs")).IsTrue();
    }

    private static void AddSettings(ConfigurationManager configuration, string? connectionString, string? otlpEndpoint) =>
        configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["APPLICATIONINSIGHTS_CONNECTION_STRING"] = connectionString,
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = otlpEndpoint,
        });

    private sealed class RecordingExporter<T>(Action<T> record) : BaseExporter<T> where T : class
    {
        public override ExportResult Export(in Batch<T> batch)
        {
            foreach (T item in batch)
                record(item);
            return ExportResult.Success;
        }
    }

    private sealed class RecordingOtlpHandler : HttpMessageHandler
    {
        public ConcurrentQueue<string> Paths { get; } = new();

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Enqueue(request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([]),
            };
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
