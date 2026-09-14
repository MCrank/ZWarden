using ZWarden.Agent.Backups;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>A configurable <see cref="IServerBackupRunner"/> double: records the call and returns a primed
/// outcome, so the command processor's backup case is tested without any filesystem.</summary>
internal sealed class FakeServerBackupRunner : IServerBackupRunner
{
    public ServerBackupOutcome Outcome { get; set; } = new(
        true, "world-20260912-100000-op-x.tar.gz", 4096, "abc123", new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero), null);

    public ServerBackupDeletionOutcome DeletionOutcome { get; set; } = new(true, null);

    public int RunCount { get; private set; }

    public int DeleteCount { get; private set; }

    public ServerId? LastServerId { get; private set; }

    public string? LastArchiveName { get; private set; }

    public Task<ServerBackupOutcome> RunAsync(ServerId serverId, OperationId operationId, CancellationToken cancellationToken)
    {
        RunCount++;
        LastServerId = serverId;
        return Task.FromResult(Outcome);
    }

    public Task<ServerBackupDeletionOutcome> DeleteAsync(ServerId serverId, string archiveName, CancellationToken cancellationToken)
    {
        DeleteCount++;
        LastServerId = serverId;
        LastArchiveName = archiveName;
        return Task.FromResult(DeletionOutcome);
    }
}
