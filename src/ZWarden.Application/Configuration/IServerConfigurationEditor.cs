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
    /// last recorded revision's hash as the drift baseline (ADR 0011). Returns the enqueued Operation on success,
    /// or a typed <see cref="ServerConfigurationResult"/> failure.
    /// </summary>
    Task<ServerConfigurationResult> ApplyAsync(
        UserId user,
        ServerId server,
        PzConfigFile file,
        IReadOnlyList<ConfigApplyEdit> edits,
        CancellationToken cancellationToken = default);
}
