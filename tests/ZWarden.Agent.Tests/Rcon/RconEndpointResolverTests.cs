using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Rcon;
using ZWarden.Agent.Tests.Docker;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Agent.Tests.Rcon;

/// <summary>
/// F18: the endpoint resolver pairs a container's ZWarden-network address (from the runtime) with the
/// Agent-owned password (from the config), or reports why it could not — no container, or RCON disabled.
/// </summary>
public class RconEndpointResolverTests
{
    private sealed class StubConfig : IRconServerConfig
    {
        public SecretString? Password { get; init; }

        public void EnsureEnabled(ServerId serverId)
        {
        }

        public SecretString? ReadPassword(ServerId serverId) => Password;
    }

    private static RconEndpointResolver Build(FakeContainerRuntime runtime, IRconServerConfig config) =>
        new(runtime, config, Options.Create(new AgentOptions
        {
            NetworkName = "zwarden",
            DataMountRoot = OperatingSystem.IsWindows() ? @"C:\pz" : "/pz",
        }));

    [Test]
    public async Task Resolves_the_endpoint_from_the_container_address_and_the_password()
    {
        var runtime = new FakeContainerRuntime { NetworkAddress = "172.20.0.9" };
        RconEndpointResolver resolver = Build(runtime, new StubConfig { Password = new SecretString("secret") });

        RconResolveResult result = await resolver.ResolveAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(RconResolveStatus.Resolved);
        await Assert.That(result.Endpoint!.Value.Host).IsEqualTo("172.20.0.9");
        await Assert.That(result.Endpoint!.Value.Port).IsEqualTo(27015);
        await Assert.That(result.Endpoint!.Value.Password.Reveal()).IsEqualTo("secret");
        await Assert.That(runtime.LastResolvedNetworkName).IsEqualTo("zwarden");
    }

    [Test]
    public async Task Reports_no_container_when_there_is_no_address()
    {
        var runtime = new FakeContainerRuntime { NetworkAddress = null };
        RconEndpointResolver resolver = Build(runtime, new StubConfig { Password = new SecretString("secret") });

        RconResolveResult result = await resolver.ResolveAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(RconResolveStatus.NoContainer);
        await Assert.That(result.Endpoint).IsNull();
    }

    [Test]
    public async Task Reports_rcon_disabled_when_the_password_is_unset()
    {
        var runtime = new FakeContainerRuntime { NetworkAddress = "172.20.0.9" };
        RconEndpointResolver resolver = Build(runtime, new StubConfig { Password = null });

        RconResolveResult result = await resolver.ResolveAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(result.Status).IsEqualTo(RconResolveStatus.RconDisabled);
    }
}
