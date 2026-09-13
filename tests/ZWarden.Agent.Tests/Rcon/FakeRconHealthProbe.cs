using ZWarden.Agent.Rcon;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Rcon;

/// <summary>
/// A configurable <see cref="IRconHealthProbe"/> double for the command-processor tests: it records the probed
/// Server and returns a primed <see cref="RconHealthResult"/>, so the processor's dispatch/dedupe/outcome
/// mapping is tested without a real RCON connection.
/// </summary>
internal sealed class FakeRconHealthProbe : IRconHealthProbe
{
    public RconHealthResult Result { get; set; } = new(Reachable: true, Authenticated: true, Detail: null);

    public int ProbeCount { get; private set; }

    public ServerId? LastServerId { get; private set; }

    public Task<RconHealthResult> ProbeAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        ProbeCount++;
        LastServerId = serverId;
        return Task.FromResult(Result);
    }
}
