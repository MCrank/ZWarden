using ZWarden.Agent.Rcon;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Tests.Rcon;

/// <summary>
/// An in-memory <see cref="IRconServerConfig"/> double: records which Servers had RCON enabled and hands back a
/// primed password, so the command processor's provision-time seeding is verified without touching the filesystem.
/// </summary>
internal sealed class FakeRconServerConfig : IRconServerConfig
{
    public List<ServerId> EnabledServers { get; } = [];

    public SecretString? Password { get; set; }

    public void EnsureEnabled(ServerId serverId) => EnabledServers.Add(serverId);

    public SecretString? ReadPassword(ServerId serverId) => Password;
}
