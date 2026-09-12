using Microsoft.Extensions.Logging;
using ZWarden.Contracts.Protocol;

namespace ZWarden.Agent.Health;

/// <summary>
/// The thread-safe, in-process <see cref="IAgentHealthState"/> (F8). Starts <see cref="AgentHealthStatus.Healthy"/>
/// — the process is up and functioning — and logs every transition with its reason, the record F16
/// later enriches and F10 puts on a heartbeat.
/// </summary>
public sealed partial class AgentHealthState : IAgentHealthState
{
    private readonly Lock _gate = new();
    private readonly ILogger<AgentHealthState> _logger;
    private AgentHealthStatus _current = AgentHealthStatus.Healthy;
    private string _reason = "Agent runtime initializing.";

    /// <summary>Creates the health state.</summary>
    public AgentHealthState(ILogger<AgentHealthState> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public AgentHealthStatus Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public string Reason
    {
        get
        {
            lock (_gate)
            {
                return _reason;
            }
        }
    }

    /// <inheritdoc />
    public void Report(AgentHealthStatus status, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        AgentHealthStatus previous;
        lock (_gate)
        {
            previous = _current;
            _current = status;
            _reason = reason;
        }

        if (previous != status)
        {
            LogTransition(previous, status, reason);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Agent health changed {Previous} -> {Current}: {Reason}")]
    private partial void LogTransition(AgentHealthStatus previous, AgentHealthStatus current, string reason);
}
