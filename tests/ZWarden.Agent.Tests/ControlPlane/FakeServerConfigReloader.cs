using ZWarden.Agent.ServerConfig;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>A fake <see cref="IServerConfigReloader"/> (#225) returning a preset attempt and counting calls.</summary>
internal sealed class FakeServerConfigReloader : IServerConfigReloader
{
    public ConfigReloadAttempt Attempt { get; set; } = new(ConfigReloadOutcome.Reloaded);

    public Exception? Throw { get; set; }

    public int CallCount { get; private set; }

    public ServerId? LastServerId { get; private set; }

    public Task<ConfigReloadAttempt> ReloadAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        CallCount++;
        LastServerId = serverId;
        return Throw is not null ? Task.FromException<ConfigReloadAttempt>(Throw) : Task.FromResult(Attempt);
    }
}
