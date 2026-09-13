using WireHealth = ZWarden.Contracts.Protocol.ServerHealth;
using DomainHealth = ZWarden.Domain.Servers.ServerHealth;

namespace ZWarden.Web.Servers;

/// <summary>
/// Maps the wire <see cref="WireHealth"/> (ZWarden.Contracts) onto the Domain's own <see cref="DomainHealth"/>
/// (F16), the health sibling of <see cref="WireServerRunState"/>. The Domain cannot reference Contracts, so this
/// bridge lives in ZWarden.Web, which sees both. The map is explicit and total: an unmapped wire value throws, so
/// adding a wire health state without teaching the Domain about it fails a test rather than silently degrading.
/// </summary>
public static class WireServerHealth
{
    /// <summary>Maps an Agent-reported wire health rollup to the Domain's last-reported health.</summary>
    public static DomainHealth ToDomain(WireHealth wire) => wire switch
    {
        WireHealth.Stopped => DomainHealth.Stopped,
        WireHealth.Starting => DomainHealth.Starting,
        WireHealth.Healthy => DomainHealth.Healthy,
        WireHealth.Degraded => DomainHealth.Degraded,
        WireHealth.Failed => DomainHealth.Failed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(wire), wire, "Unmapped wire ServerHealth; teach the Domain about it (F16)."),
    };
}
