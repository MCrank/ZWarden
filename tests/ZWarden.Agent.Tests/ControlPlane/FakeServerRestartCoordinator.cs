using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.Servers;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>A fake <see cref="IServerRestartCoordinator"/> that records the graceful-restart calls (#114) without
/// touching RCON or Docker — used where the caller drives its own restart (F17) and only needs the pre-stop
/// broadcast to be invoked.</summary>
internal sealed class FakeServerRestartCoordinator : IServerRestartCoordinator
{
    public int WarnCount { get; private set; }

    public int RestartCount { get; private set; }

    public ServerId? LastWarnedServerId { get; private set; }

    public GracefulRestartPlan? LastPlan { get; private set; }

    public Task WarnAsync(
        ServerId serverId,
        GracefulRestartPlan? plan,
        OperationId operationId,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken)
    {
        WarnCount++;
        LastWarnedServerId = serverId;
        LastPlan = plan;
        return Task.CompletedTask;
    }

    public Task RestartAsync(
        ServerId serverId,
        GracefulRestartPlan? plan,
        OperationId operationId,
        IOperationProgressReporter progress,
        CancellationToken cancellationToken)
    {
        RestartCount++;
        LastPlan = plan;
        return Task.CompletedTask;
    }
}
