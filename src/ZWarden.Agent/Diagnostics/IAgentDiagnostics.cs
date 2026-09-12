using ZWarden.Contracts.Protocol;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Diagnostics;

/// <summary>
/// A point-in-time diagnostic snapshot of the Agent runtime (F8): who it is, what protocol it
/// speaks, how it is, and how long it has run. The minimal surface F9/F10/F16 attach richer
/// diagnostics to.
/// </summary>
/// <param name="AgentId">The Agent's resolved self-identity.</param>
/// <param name="ProtocolVersion">The protocol version this build speaks (F7).</param>
/// <param name="Health">The Agent's current self-reported health.</param>
/// <param name="HealthReason">Why the Agent is in that health state.</param>
/// <param name="Uptime">How long the runtime has been up.</param>
public sealed record AgentDiagnosticsSnapshot(
    AgentId AgentId,
    int ProtocolVersion,
    AgentHealthStatus Health,
    string HealthReason,
    TimeSpan Uptime);

/// <summary>Captures the current <see cref="AgentDiagnosticsSnapshot"/> (F8).</summary>
public interface IAgentDiagnostics
{
    /// <summary>Captures the Agent runtime's current diagnostic snapshot.</summary>
    AgentDiagnosticsSnapshot Capture();
}
