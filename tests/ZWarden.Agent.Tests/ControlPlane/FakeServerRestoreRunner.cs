using ZWarden.Agent.Backups;
using ZWarden.Agent.ControlPlane;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>A configurable <see cref="IServerRestoreRunner"/> double: records the call and returns a primed
/// outcome, so the command processor's restore case is tested without any filesystem or Docker.</summary>
internal sealed class FakeServerRestoreRunner : IServerRestoreRunner
{
    public ServerRestoreOutcome Outcome { get; set; } = new(
        true,
        "world-20260914-100000-op.tar.gz",
        "world-20260914-095900-op-pre-restore.tar.gz",
        8192,
        "def456",
        new DateTimeOffset(2026, 9, 14, 9, 59, 0, TimeSpan.Zero),
        null);

    public int RunCount { get; private set; }

    public ServerId? LastServerId { get; private set; }

    public string? LastArchiveName { get; private set; }

    public string? LastExpectedSha256 { get; private set; }

    public Task<ServerRestoreOutcome> RunAsync(
        ServerId serverId,
        OperationId operationId,
        string archiveName,
        string expectedSha256,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken)
    {
        RunCount++;
        LastServerId = serverId;
        LastArchiveName = archiveName;
        LastExpectedSha256 = expectedSha256;
        return Task.FromResult(Outcome);
    }
}
