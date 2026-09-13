using ZWarden.Agent.ControlPlane;
using ZWarden.Agent.SteamCmd;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>A configurable <see cref="IServerUpdateRunner"/> double: records the call and returns a primed
/// outcome, so the command processor's update case is tested without any Docker or filesystem.</summary>
internal sealed class FakeServerUpdateRunner : IServerUpdateRunner
{
    public ServerUpdateOutcome Outcome { get; set; } = new(true, "24909836", null);

    public int RunCount { get; private set; }

    public ServerId? LastServerId { get; private set; }

    public Task<ServerUpdateOutcome> RunAsync(
        ServerId serverId, OperationId operationId, IOperationProgressReporter progress, CancellationToken cancellationToken)
    {
        RunCount++;
        LastServerId = serverId;
        return Task.FromResult(Outcome);
    }
}
