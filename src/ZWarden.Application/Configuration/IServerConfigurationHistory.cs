using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Configuration;

/// <summary>
/// The operator-facing read seam over a Server's Configuration Revision history (F20b PR-4). The control plane
/// holds no live config file — only the value snapshots recorded after each write (ADR 0011) — so this returns
/// the recorded revisions, newest first, each with its value-level diff from the preceding revision already
/// computed. Fail-closed (ADR 0018): it resolves the Server through the tenant filter and authorizes the
/// server-scoped <c>ServerConfigurationEdit</c> permission; an unknown Server or an unauthorized caller sees an
/// empty history rather than another tenant's data.
/// </summary>
public interface IServerConfigurationHistory
{
    /// <summary>
    /// The recorded revisions of a Server's <paramref name="file"/> for <paramref name="user"/>, newest first,
    /// each carrying its diff from the revision before it. Empty when the Server is not visible to the caller's
    /// tenant, the caller lacks <c>ServerConfigurationEdit</c>, or nothing has been recorded yet.
    /// </summary>
    Task<IReadOnlyList<ConfigRevisionView>> GetHistoryAsync(
        UserId user,
        ServerId server,
        PzConfigFile file,
        CancellationToken cancellationToken = default);
}
