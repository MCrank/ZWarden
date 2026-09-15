using ZWarden.Application.Diagnostics;
using ZWarden.Contracts.Protocol;
using Wire = ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Web.Diagnostics;

/// <summary>
/// Maps a completed gather's wire bundle (Contracts) onto the engine's domain-level checks (Application) at the
/// Web boundary (F29). The Application layer references no wire types, so the wire <c>ProbeStatus</c> and wire
/// <c>DiagnosticDomain</c> are translated here — the same seam <c>WireServerHealth</c> occupies for F16. The
/// untrusted detail is carried through unchanged (already bounded by the Agent); it is escaped at render.
/// </summary>
public static class WireDiagnostics
{
    /// <summary>Maps a gather's wire checks to domain checks.</summary>
    public static IReadOnlyList<DiagnosticCheck> ToChecks(IReadOnlyList<Wire.DiagnosticCheckFact> facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return facts.Select(f => new DiagnosticCheck(ToDomain(f.Domain), ToStatus(f.Status), f.Summary, f.Detail)).ToList();
    }

    private static DiagnosticStatus ToStatus(ProbeStatus status) => status switch
    {
        ProbeStatus.Pass => DiagnosticStatus.Pass,
        ProbeStatus.Warn => DiagnosticStatus.Warn,
        ProbeStatus.Fail => DiagnosticStatus.Fail,
        ProbeStatus.Skipped => DiagnosticStatus.Skipped,
        _ => DiagnosticStatus.Skipped,
    };

    private static DiagnosticDomain ToDomain(Wire.DiagnosticDomain domain) => domain switch
    {
        Wire.DiagnosticDomain.Docker => DiagnosticDomain.Docker,
        Wire.DiagnosticDomain.Rcon => DiagnosticDomain.Rcon,
        Wire.DiagnosticDomain.GamePort => DiagnosticDomain.GamePort,
        Wire.DiagnosticDomain.Filesystem => DiagnosticDomain.Filesystem,
        Wire.DiagnosticDomain.SteamCmd => DiagnosticDomain.SteamCmd,
        Wire.DiagnosticDomain.Mod => DiagnosticDomain.Mod,
        Wire.DiagnosticDomain.Config => DiagnosticDomain.Config,
        Wire.DiagnosticDomain.Compatibility => DiagnosticDomain.Compatibility,
        _ => DiagnosticDomain.Filesystem,
    };
}
