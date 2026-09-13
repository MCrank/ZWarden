using ZWarden.Agent.Rcon;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Rcon;

namespace ZWarden.Agent.Tests.Rcon;

/// <summary>
/// F18: the RCON health probe maps a connection attempt into a legible <see cref="RconHealthResult"/> — healthy,
/// RCON-disabled, no-container, password-rejected, timed-out, or unreachable — and always closes the socket so a
/// probe never leaks one of PZ's five connection slots.
/// </summary>
public class RconHealthProbeTests
{
    private static readonly RconEndpoint AnyEndpoint = new("172.20.0.5", 27015, new SecretString("pw"));

    private static (RconHealthProbe Probe, FakeRconConnection Connection) Build(
        RconResolveResult resolution, Exception? connectException = null)
    {
        var connection = new FakeRconConnection { ConnectException = connectException };
        var resolver = new FakeRconEndpointResolver { Result = resolution };
        var probe = new RconHealthProbe(resolver, new FakeRconConnectionFactory(connection));
        return (probe, connection);
    }

    [Test]
    public async Task Healthy_when_the_connection_authenticates()
    {
        (RconHealthProbe probe, FakeRconConnection connection) = Build(RconResolveResult.Resolved(AnyEndpoint));

        RconHealthResult result = await probe.ProbeAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(result.Reachable).IsTrue();
        await Assert.That(result.Authenticated).IsTrue();
        await Assert.That(result.Detail).IsNull();
        await Assert.That(connection.DisposeCount).IsEqualTo(1); // socket always closed
    }

    [Test]
    public async Task Disabled_when_no_password_is_configured()
    {
        (RconHealthProbe probe, FakeRconConnection connection) = Build(RconResolveResult.RconDisabled);

        RconHealthResult result = await probe.ProbeAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(result.Reachable).IsFalse();
        await Assert.That(result.Authenticated).IsFalse();
        await Assert.That(result.Detail).Contains("disabled");
        await Assert.That(connection.ConnectCount).IsEqualTo(0); // no connection attempted
    }

    [Test]
    public async Task No_container_reports_unreachable_without_connecting()
    {
        (RconHealthProbe probe, FakeRconConnection connection) = Build(RconResolveResult.NoContainer);

        RconHealthResult result = await probe.ProbeAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(result.Reachable).IsFalse();
        await Assert.That(result.Detail).Contains("No running container");
        await Assert.That(connection.ConnectCount).IsEqualTo(0);
    }

    [Test]
    public async Task Reachable_but_not_authenticated_when_the_password_is_rejected()
    {
        (RconHealthProbe probe, FakeRconConnection connection) = Build(
            RconResolveResult.Resolved(AnyEndpoint), new RconAuthenticationException("rejected"));

        RconHealthResult result = await probe.ProbeAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(result.Reachable).IsTrue();
        await Assert.That(result.Authenticated).IsFalse();
        await Assert.That(result.Detail).Contains("rejected");
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Unreachable_on_a_timeout()
    {
        (RconHealthProbe probe, FakeRconConnection connection) = Build(
            RconResolveResult.Resolved(AnyEndpoint), new RconTimeoutException("timed out"));

        RconHealthResult result = await probe.ProbeAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(result.Reachable).IsFalse();
        await Assert.That(result.Detail).Contains("Timed out");
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }

    [Test]
    public async Task Unreachable_when_the_connection_is_refused_or_capped()
    {
        (RconHealthProbe probe, FakeRconConnection connection) = Build(
            RconResolveResult.Resolved(AnyEndpoint), new RconException("refused"));

        RconHealthResult result = await probe.ProbeAsync(ServerId.New(), CancellationToken.None);

        await Assert.That(result.Reachable).IsFalse();
        await Assert.That(result.Authenticated).IsFalse();
        await Assert.That(connection.DisposeCount).IsEqualTo(1);
    }
}
