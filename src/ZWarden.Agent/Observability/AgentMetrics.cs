using System.Diagnostics.Metrics;

namespace ZWarden.Agent.Observability;

/// <summary>
/// ZWarden.Agent's custom OpenTelemetry instruments (F16, ADR 0024). It owns the Agent <see cref="Meter"/> whose
/// name (<see cref="AgentTelemetry.MeterName"/>) the pipeline exports. A singleton; kept small on purpose —
/// HttpClient auto-instrumentation covers the rest of the baseline.
/// </summary>
public sealed class AgentMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _metricsSweeps;

    public AgentMetrics()
    {
        _meter = new Meter(AgentTelemetry.MeterName);
        _metricsSweeps = _meter.CreateCounter<long>(
            "zwarden.agent.metrics_sweeps",
            unit: "{sweep}",
            description: "Runtime-metrics sweeps the Agent completed and reported.");
    }

    /// <summary>Records one completed metrics sweep that reported <paramref name="serverCount"/> servers.</summary>
    public void RecordMetricsSweep(int serverCount) =>
        _metricsSweeps.Add(1, new KeyValuePair<string, object?>("zwarden.server.count", serverCount));

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
