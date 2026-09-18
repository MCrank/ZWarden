using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Configuration;

/// <summary>
/// The operator-facing entry point for applying surgical value edits to a Server's configuration files (F20b
/// PR-3). It is the enqueue half: authorize, validate, capture the drift baseline, and enqueue a mutating
/// Operation carrying the edits; the Agent does the drift-checked, byte-preserving, atomic write and reports a
/// Configuration Revision on completion. PR-4's structured editor is a caller of this.
/// </summary>
public interface IServerConfigurationEditor
{
    /// <summary>
    /// Applies <paramref name="edits"/> to a Server's <paramref name="file"/> on behalf of
    /// <paramref name="user"/>. Fail-closed (ADR 0018): resolves the Server through the tenant filter, authorizes
    /// <c>ServerConfigurationEdit</c> against it, and validates the edits before enqueueing a <b>mutating,
    /// server-scoped</b> Operation (per-server lock, ADR 0022) whose command payload carries the edits and the
    /// drift baseline the Agent re-checks (ADR 0011). Returns the enqueued Operation on success, or a typed
    /// <see cref="ServerConfigurationResult"/> failure.
    /// <para>
    /// <paramref name="expectedBaselineHash"/> is the interactive editor's <b>live-read baseline</b> (F20c, ADR
    /// 0042): the canonical value hash of exactly the state the operator was shown, so the Agent drift-checks
    /// against what they saw rather than the last recorded revision — the false positive after an in-game edit is
    /// gone, and a change between the read and this apply is still caught (the write stays fail-closed, never
    /// fail-open). When it is <c>null</c> (a non-interactive caller such as the mod manager) the enqueuer falls
    /// back to the last recorded revision's hash exactly as before.
    /// </para>
    /// </summary>
    Task<ServerConfigurationResult> ApplyAsync(
        UserId user,
        ServerId server,
        PzConfigFile file,
        IReadOnlyList<ConfigApplyEdit> edits,
        string? expectedBaselineHash = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a Server's configuration file to the values of a prior revision (F20b PR-4). Fail-closed like
    /// <see cref="ApplyAsync"/>: it resolves the target <paramref name="revision"/> through the tenant filter,
    /// authorizes <c>ServerConfigurationEdit</c> on the Server, then computes the value-level edits that move
    /// the file's <b>current</b> recorded state to the target revision's — only the differing scalars, never a
    /// whole-file resend (the payload cap forbids that, and PZ owns the key set: structural add/remove is
    /// reported, not applied). The edits ride a mutating Operation exactly as an apply does; the Agent still
    /// drift-checks against the current baseline and writes byte-preservingly. Returns the enqueued Operation,
    /// or a typed <see cref="ServerConfigurationResult"/> failure (including <c>InvalidInput</c> when the
    /// revision already matches the current state or differs only structurally).
    /// </summary>
    Task<ServerConfigurationResult> RestoreAsync(
        UserId user,
        ServerId server,
        ConfigurationRevisionId revision,
        CancellationToken cancellationToken = default);
}
