using ZWarden.Application.Servers;
using WireBreakdown = ZWarden.Contracts.Protocol.Messages.HealthBreakdown;
using WireProbeCheck = ZWarden.Contracts.Protocol.Messages.ProbeCheck;
using WireProbeStatus = ZWarden.Contracts.Protocol.ProbeStatus;
using DomainProbeStatus = ZWarden.Domain.Servers.ProbeStatus;

namespace ZWarden.Web.Servers;

/// <summary>
/// Maps the wire <see cref="WireBreakdown"/> (ZWarden.Contracts) onto the transient live-cache
/// <see cref="LiveHealthBreakdown"/> (ZWarden.Application) — the probe-level sibling of <see cref="WireServerHealth"/>
/// (F16, #93). Application cannot reference Contracts, so this bridge lives in ZWarden.Web, which sees both. The
/// status map is explicit and total: an unmapped wire value throws, so adding a wire probe status without teaching
/// the live cache about it fails a test rather than silently degrading. Each detail string is untrusted Agent
/// output (trust-boundaries.md §8); carried across verbatim and rendered escaped, never interpreted.
/// </summary>
public static class WireHealthBreakdown
{
    /// <summary>Maps an Agent-reported wire breakdown to the live-cache breakdown the panel reads.</summary>
    public static LiveHealthBreakdown ToLive(WireBreakdown wire)
    {
        ArgumentNullException.ThrowIfNull(wire);
        return new LiveHealthBreakdown(
            ToLive(wire.Container),
            ToLive(wire.Process),
            ToLive(wire.Startup),
            ToLive(wire.Network));
    }

    /// <summary>Maps one wire probe check to its live-cache verdict.</summary>
    public static ProbeVerdict ToLive(WireProbeCheck check)
    {
        ArgumentNullException.ThrowIfNull(check);
        return new ProbeVerdict(ToDomain(check.Status), check.Detail);
    }

    /// <summary>Maps an Agent-reported wire probe status to the Domain's own probe status.</summary>
    public static DomainProbeStatus ToDomain(WireProbeStatus wire) => wire switch
    {
        WireProbeStatus.Pass => DomainProbeStatus.Pass,
        WireProbeStatus.Warn => DomainProbeStatus.Warn,
        WireProbeStatus.Fail => DomainProbeStatus.Fail,
        WireProbeStatus.Skipped => DomainProbeStatus.Skipped,
        _ => throw new ArgumentOutOfRangeException(
            nameof(wire), wire, "Unmapped wire ProbeStatus; teach the live health cache about it (F16, #93)."),
    };
}
