using ZWarden.Agent.Mods;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Mods;

/// <summary>A scripted <see cref="IModDiscovery"/> for the command-processor tests: it records the Server it was
/// asked about and returns a canned result.</summary>
internal sealed class FakeModDiscovery : IModDiscovery
{
    public ModDiscoveryResult Result { get; set; } = new([], [], [], []);

    public ServerId? DiscoveredFor { get; private set; }

    public int CallCount { get; private set; }

    public Task<ModDiscoveryResult> DiscoverAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        DiscoveredFor = serverId;
        CallCount++;
        return Task.FromResult(Result);
    }
}
