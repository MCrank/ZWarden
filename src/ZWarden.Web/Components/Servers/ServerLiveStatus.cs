using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;

namespace ZWarden.Web.Components.Servers;

/// <summary>
/// What the server-detail header shows right now (#249): the badge's label and tone, whether a mutating Operation
/// holds the Server's lock, and which lifecycle actions the current state allows. Permission is separate — the page
/// renders only the buttons the operator holds; this only says which of them are enabled.
/// </summary>
/// <param name="Label">The badge label (uppercase), e.g. <c>RUNNING</c> or <c>RESTARTING</c>.</param>
/// <param name="Tone">The run-state whose status colour the badge wears.</param>
/// <param name="Busy">A mutating Operation holds the per-server lock (ADR 0022).</param>
/// <param name="CanStart">Start is allowed by the current state.</param>
/// <param name="CanStop">Stop is allowed by the current state.</param>
/// <param name="CanRestart">Restart is allowed by the current state.</param>
public sealed record ServerStatusView(
    string Label, ServerRunState Tone, bool Busy, bool CanStart, bool CanStop, bool CanRestart)
{
    /// <summary>The status-ramp key (style-guide.md) the live-status script maps to the badge's colour classes.</summary>
    public string ToneKey => Tone switch
    {
        ServerRunState.Running => "running",
        ServerRunState.Stopped => "stopped",
        ServerRunState.Starting or ServerRunState.Stopping => "busy",
        ServerRunState.Failed => "unhealthy",
        _ => "unknown",
    };
}

/// <summary>
/// Resolves the header's <see cref="ServerStatusView"/> from the Agent-observed run-state and the Server's
/// in-flight mutating Operation (#249). The observation alone can't show a safe stop — the container stays up (and
/// is reported Running) for the whole stop grace — so a lifecycle Operation in flight supplies the transitional
/// label. Any mutating Operation holding the lock disables every lifecycle button, since the service would refuse
/// it as busy; otherwise the buttons follow the observed state, as the static header always did.
/// </summary>
public static class ServerLiveStatus
{
    public static ServerStatusView Resolve(ServerRunState observed, OperationKind? activeOperation)
    {
        string? transition = activeOperation switch
        {
            OperationKind.StopServer => "STOPPING",
            OperationKind.RestartServer => "RESTARTING",
            OperationKind.StartServer => "STARTING",
            OperationKind.UpdateServer => "UPDATING",
            OperationKind.Restore => "RESTORING",
            _ => null,
        };

        if (activeOperation is not null)
        {
            return transition is null
                ? new ServerStatusView(Label(observed), observed, Busy: true, false, false, false)
                : new ServerStatusView(transition, ServerRunState.Starting, Busy: true, false, false, false);
        }

        return new ServerStatusView(
            Label(observed),
            observed,
            Busy: false,
            CanStart: observed is ServerRunState.Stopped or ServerRunState.Failed or ServerRunState.Unknown,
            CanStop: observed is ServerRunState.Running or ServerRunState.Starting,
            CanRestart: observed is ServerRunState.Running);
    }

    /// <summary>Indexes the tenant's in-flight mutating Operations by Server (#253) — at most one each, the lock
    /// holder — so a fleet of rows resolves from one read.</summary>
    public static IReadOnlyDictionary<ServerId, OperationKind> ActiveByServer(IEnumerable<Operation> active)
        => active
            .Where(o => o.ServerId is not null)
            .GroupBy(o => o.ServerId!.Value)
            .ToDictionary(g => g.Key, g => g.First().Kind);

    /// <summary>Resolves one fleet row against <see cref="ActiveByServer"/>'s index.</summary>
    public static ServerStatusView Resolve(ServerRunState observed, ServerId server, IReadOnlyDictionary<ServerId, OperationKind> activeByServer)
        => Resolve(observed, activeByServer.TryGetValue(server, out OperationKind kind) ? kind : null);

    private static string Label(ServerRunState state) => state.ToString().ToUpperInvariant();
}
