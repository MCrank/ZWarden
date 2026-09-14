using ZWarden.Application.Configuration;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Configuration;

/// <summary>
/// The default <see cref="IConfigurationRevisionRecorder"/> (F20b PR-3). It mirrors the other completion-time
/// reconcilers (<see cref="ServerStateReconciler"/>): resolve the Server through the tenant filter and no-op
/// when it is not this tenant's, then record the revision the Agent reported and save. The snapshot and hash
/// were computed by <c>ZWarden.PzConfig</c> on the Agent from the live file after the write — the domain takes
/// no dependency on the parser (ReferenceDirectionTests §9 rule 2); it stores the values it was handed.
/// </summary>
public sealed class ConfigurationRevisionRecorder : IConfigurationRevisionRecorder
{
    private readonly ZWardenDbContext _context;
    private readonly ServerRepository _servers;
    private readonly ConfigurationRevisionRepository _revisions;
    private readonly TimeProvider _clock;

    public ConfigurationRevisionRecorder(
        ZWardenDbContext context,
        ServerRepository servers,
        ConfigurationRevisionRepository revisions,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(revisions);
        ArgumentNullException.ThrowIfNull(clock);
        _context = context;
        _servers = servers;
        _revisions = revisions;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task RecordAsync(
        ServerId serverId,
        PzConfigFile file,
        string canonicalSnapshot,
        string snapshotHash,
        CancellationToken cancellationToken = default)
    {
        Server? server = await _servers.FindByIdAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (server is null)
        {
            // A completion for a Server this tenant does not own (or that was removed): no-op.
            return;
        }

        _revisions.Add(ConfigurationRevision.Record(
            serverId, file, canonicalSnapshot, snapshotHash, _clock.GetUtcNow(), createdBy: null));
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
