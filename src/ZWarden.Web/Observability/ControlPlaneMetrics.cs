using System.Diagnostics.Metrics;
using ZWarden.Contracts.Protocol;

namespace ZWarden.Web.Observability;

/// <summary>
/// ZWarden.Web's custom OpenTelemetry instruments (F16, ADR 0024). It owns the control-plane <see cref="Meter"/>
/// whose name (<see cref="ZWardenTelemetry.MeterName"/>) the telemetry pipeline exports. A singleton, so the
/// instruments live for the process; auto-instrumentation (ASP.NET Core, HttpClient) covers the rest of the
/// baseline. Kept small on purpose — a baseline, not a full metrics surface.
/// </summary>
public sealed class ControlPlaneMetrics : IDisposable
{
    private readonly Meter _meter;
    private readonly Counter<long> _healthTransitions;

    public ControlPlaneMetrics()
    {
        _meter = new Meter(ZWardenTelemetry.MeterName);
        _healthTransitions = _meter.CreateCounter<long>(
            "zwarden.server.health_transitions",
            unit: "{transition}",
            description: "Server health-rollup transitions ZWarden.Web observed from Agents.");
    }

    /// <summary>Records one observed server-health transition, tagged with the new health.</summary>
    public void RecordHealthTransition(ServerHealth health) =>
        _healthTransitions.Add(1, new KeyValuePair<string, object?>("zwarden.server.health", health.ToString()));

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}
