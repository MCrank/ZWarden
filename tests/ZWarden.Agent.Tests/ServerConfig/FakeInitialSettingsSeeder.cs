using ZWarden.Agent.ServerConfig;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ServerConfig;

/// <summary>
/// An in-memory <see cref="IInitialSettingsSeeder"/> double (#230): records what was seeded for which Server, and can
/// run a hook so a test can assert the seed happens before the container is created.
/// </summary>
internal sealed class FakeInitialSettingsSeeder : IInitialSettingsSeeder
{
    public List<(ServerId Server, InitialServerSettings Settings)> Seeded { get; } = [];

    public Action? OnSeed { get; set; }

    public void Seed(ServerId serverId, InitialServerSettings settings)
    {
        OnSeed?.Invoke();
        Seeded.Add((serverId, settings));
    }
}
