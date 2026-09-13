using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ZWarden.Web.Observability;

/// <summary>
/// The OpenTelemetry baseline for ZWarden.Web (F16, ADR 0024): the SDK, a resource identifying the service, the
/// ZWarden-owned <see cref="Meter"/>/<see cref="ActivitySource"/>, ASP.NET Core + HttpClient auto-instrumentation,
/// and exporters. <b>The OTLP exporter is opt-in</b> — it is added only when an endpoint is configured
/// (<c>ZWarden:Observability:OtlpEndpoint</c> or the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>), so a default
/// install sends nothing off-box. The Console exporter is added in Development for local visibility. No Prometheus
/// exporter ships (F16 non-goal).
/// </summary>
public static class ZWardenTelemetry
{
    /// <summary>The service name stamped on every exported signal.</summary>
    public const string ServiceName = "zwarden-web";

    /// <summary>The ZWarden-owned meter name the pipeline exports (see <see cref="ControlPlaneMetrics"/>).</summary>
    public const string MeterName = "ZWarden.Web";

    /// <summary>The ZWarden-owned activity source name the pipeline exports (available for custom spans).</summary>
    public const string ActivitySourceName = "ZWarden.Web";

    /// <summary>Registers the ZWarden.Web observability baseline.</summary>
    public static IServiceCollection AddZWardenTelemetry(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton<ControlPlaneMetrics>();

        string? otlpEndpoint = OtlpEndpoint(configuration);
        bool console = environment.IsDevelopment();

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(ServiceName))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
                if (otlpEndpoint is not null)
                {
                    metrics.AddOtlpExporter();
                }

                if (console)
                {
                    metrics.AddConsoleExporter();
                }
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(ActivitySourceName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
                if (otlpEndpoint is not null)
                {
                    tracing.AddOtlpExporter();
                }

                if (console)
                {
                    tracing.AddConsoleExporter();
                }
            });

        return services;
    }

    // Opt-in: OTLP is wired only when an endpoint is explicitly configured, so nothing leaves the box by default.
    private static string? OtlpEndpoint(IConfiguration configuration)
    {
        string? endpoint = configuration["ZWarden:Observability:OtlpEndpoint"]
            ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        return string.IsNullOrWhiteSpace(endpoint) ? null : endpoint;
    }
}
