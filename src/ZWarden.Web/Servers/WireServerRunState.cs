using WireRunState = ZWarden.Contracts.Protocol.ServerRunState;
using DomainRunState = ZWarden.Domain.Servers.ServerRunState;

namespace ZWarden.Web.Servers;

/// <summary>
/// Maps the wire <see cref="WireRunState"/> (ZWarden.Contracts) onto the Domain's own
/// <see cref="DomainRunState"/> (F14, D4). The Domain cannot reference Contracts, so this bridge lives in
/// ZWarden.Web, which sees both. The map is explicit and total: an unmapped wire value throws, so adding a
/// wire state without teaching the Domain about it fails a test rather than silently degrading to Unknown.
/// </summary>
public static class WireServerRunState
{
    /// <summary>Maps an Agent-reported wire run-state to the Domain's last-reported run-state.</summary>
    public static DomainRunState ToDomain(WireRunState wire) => wire switch
    {
        WireRunState.Unknown => DomainRunState.Unknown,
        WireRunState.Stopped => DomainRunState.Stopped,
        WireRunState.Starting => DomainRunState.Starting,
        WireRunState.Running => DomainRunState.Running,
        WireRunState.Stopping => DomainRunState.Stopping,
        WireRunState.Failed => DomainRunState.Failed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(wire), wire, "Unmapped wire ServerRunState; teach the Domain about it (F14 D4)."),
    };
}
