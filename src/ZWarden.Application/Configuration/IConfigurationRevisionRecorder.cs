using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Configuration;

/// <summary>
/// Records a <see cref="ConfigurationRevision"/> from a successful configuration apply the Agent reported (F20b
/// PR-3). The Agent, the only party that reads the live <c>/pz/</c> file, returns the canonical value snapshot
/// and hash of the file <b>after</b> the surgical write; this records that against the Server as the new drift
/// baseline (ADR 0011). Called from the completion path, which has no acting user, so the revision is recorded
/// unattributed — the audit trail (F6) carries who applied it.
/// </summary>
public interface IConfigurationRevisionRecorder
{
    /// <summary>
    /// Records a revision of <paramref name="file"/> on <paramref name="serverId"/> from the Agent-reported
    /// canonical snapshot and its hash. A no-op when the Server is not in the ambient tenant (observed data is
    /// applied only to a Server this tenant owns — trust-boundaries.md §3), mirroring the other completion-time
    /// reconcilers.
    /// </summary>
    Task RecordAsync(
        ServerId serverId,
        PzConfigFile file,
        string canonicalSnapshot,
        string snapshotHash,
        CancellationToken cancellationToken = default);
}
