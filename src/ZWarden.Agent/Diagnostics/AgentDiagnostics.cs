using ZWarden.Agent.Health;
using ZWarden.Agent.Identity;
using ZWarden.Contracts.Protocol;

namespace ZWarden.Agent.Diagnostics;

/// <summary>
/// The default <see cref="IAgentDiagnostics"/> (F8). Uptime is measured from construction against an
/// injected <see cref="TimeProvider"/>, so it stays testable rather than reading a hidden clock.
/// </summary>
public sealed class AgentDiagnostics : IAgentDiagnostics
{
    private readonly IAgentIdentity _identity;
    private readonly IAgentHealthState _health;
    private readonly TimeProvider _timeProvider;
    private readonly DateTimeOffset _startedAt;

    /// <summary>Creates the diagnostics service and marks the runtime's start time.</summary>
    public AgentDiagnostics(IAgentIdentity identity, IAgentHealthState health, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(health);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _identity = identity;
        _health = health;
        _timeProvider = timeProvider;
        _startedAt = timeProvider.GetUtcNow();
    }

    /// <inheritdoc />
    public AgentDiagnosticsSnapshot Capture() => new(
        _identity.AgentId,
        ProtocolVersion.Current,
        _health.Current,
        _health.Reason,
        _timeProvider.GetUtcNow() - _startedAt);
}
