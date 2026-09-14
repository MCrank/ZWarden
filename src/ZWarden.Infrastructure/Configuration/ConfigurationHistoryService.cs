using ZWarden.Application.Authorization;
using ZWarden.Application.Configuration;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Servers;
using ZWarden.PzConfig.Model;
using ZWarden.PzConfig.Revisions;

namespace ZWarden.Infrastructure.Configuration;

/// <summary>
/// The tenant-scoped read side of the configuration UI (F20b PR-4): a Server's Configuration Revision history for
/// one file, newest first, each revision carrying its value-level diff from the one before it (ADR 0011). The
/// control plane holds no live config file, so the diff is computed from the persisted canonical snapshots — read
/// back with <c>PzValueSnapshot.Parse</c> and compared with <c>PzValueDiff</c>, neither of which touches the
/// parser. Fail-closed (ADR 0018): it resolves the Server through the tenant filter and authorizes the
/// server-scoped <c>ServerConfigurationEdit</c> permission, returning an empty history rather than another
/// tenant's data on an unknown Server or an unauthorized caller.
/// </summary>
public sealed class ConfigurationHistoryService : IServerConfigurationHistory
{
    private const int ShortHashLength = 12;

    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly ConfigurationRevisionRepository _revisions;

    public ConfigurationHistoryService(
        ServerRepository servers,
        IPermissionChecker permissions,
        ConfigurationRevisionRepository revisions)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(revisions);
        _servers = servers;
        _permissions = permissions;
        _revisions = revisions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConfigRevisionView>> GetHistoryAsync(
        UserId user, ServerId server, PzConfigFile file, CancellationToken cancellationToken = default)
    {
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return [];
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ServerConfigurationEdit, server: server, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return [];
        }

        // Newest first. Each snapshot is parsed once; a revision's diff is against the next-older revision.
        IReadOnlyList<ConfigurationRevision> revisions =
            await _revisions.ListForFileAsync(server, file, cancellationToken).ConfigureAwait(false);

        var snapshots = new PzValueSnapshot[revisions.Count];
        for (int i = 0; i < revisions.Count; i++)
        {
            snapshots[i] = PzValueSnapshot.Parse(revisions[i].CanonicalSnapshot);
        }

        var views = new List<ConfigRevisionView>(revisions.Count);
        for (int i = 0; i < revisions.Count; i++)
        {
            ConfigurationRevision revision = revisions[i];
            IReadOnlyList<ConfigValueChange> changes = i + 1 < revisions.Count
                ? [.. PzValueDiff.Compare(snapshots[i + 1], snapshots[i]).Select(ToChange)]
                : [];

            views.Add(new ConfigRevisionView(
                revision.Id,
                revision.File,
                revision.CreatedAt,
                ShortHash(revision.SnapshotHash),
                IsCurrent: i == 0,
                changes));
        }

        return views;
    }

    private static ConfigValueChange ToChange(PzConfigChange change) => new(
        change.Path,
        change.Kind switch
        {
            PzConfigChangeKind.Added => ConfigChangeKind.Added,
            PzConfigChangeKind.Removed => ConfigChangeKind.Removed,
            _ => ConfigChangeKind.Changed,
        },
        Render(change.Before),
        Render(change.After));

    private static string? Render(PzValue? value) => value switch
    {
        null => null,
        PzBoolean b => b.Value ? "true" : "false",
        PzNumber n => n.Lexeme,
        PzString s => s.Value,
        _ => value.ToString(),
    };

    private static string ShortHash(string hash) =>
        hash.Length <= ShortHashLength ? hash : hash[..ShortHashLength];
}
