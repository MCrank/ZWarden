using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ZWarden.Agent.Observability;

/// <summary>
/// The OpenTelemetry baseline for ZWarden.Agent (F16, ADR 0024): the SDK, a resource identifying the service, the
/// ZWarden-owned <see cref="Meter"/>/<see cref="ActivitySource"/>, HttpClient auto-instrumentation (the Agent
/// hosts no inbound server, so no ASP.NET Core instrumentation), and exporters. <b>OTLP is opt-in</b> — added only
/// when an endpoint is configured (<c>Agent:Observability:OtlpEndpoint</c> or <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>) —
/// and the Console exporter is added in Development. No Prometheus exporter ships (F16 non-goal).
/// </summary>
public static class AgentTelemetry
{
    /// <summary>The service name stamped on every exported signal.</summary>
    public const string ServiceName = "zwarden-agent";

    /// <summary>The ZWarden-owned meter name the pipeline exports (see <see cref="AgentMetrics"/>).</summary>
    public const string MeterName = "ZWarden.Agent";

    /// <summary>The ZWarden-owned activity source name the pipeline exports (available for custom spans).</summary>
    public const string ActivitySourceName = "ZWarden.Agent";

    /// <summary>Registers the ZWarden.Agent observability baseline.</summary>
    public static IServiceCollection AddAgentTelemetry(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        services.AddSingleton<AgentMetrics>();

        string? otlpEndpoint = OtlpEndpoint(configuration);
        bool console = environment.IsDevelopment();

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(ServiceName))
            .WithMetrics(metrics =>
            {
                metrics.AddMeter(MeterName).AddHttpClientInstrumentation();
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
                tracing.AddSource(ActivitySourceName).AddHttpClientInstrumentation();
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

    private static string? OtlpEndpoint(IConfiguration configuration)
    {
        string? endpoint = configuration["Agent:Observability:OtlpEndpoint"]
            ?? configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        return string.IsNullOrWhiteSpace(endpoint) ? null : endpoint;
    }
}
