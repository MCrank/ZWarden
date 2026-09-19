using ZWarden.Agent.Health;

namespace ZWarden.Agent.Tests.Health;

/// <summary>
/// #199: the probe now takes the address to reach (the PZ container's ZWarden-network IP, not loopback). These
/// offline cases pin the input guards — a null/empty/unparseable host or an out-of-range port returns <c>null</c>
/// (unknown), never <c>false</c>, so a probe that cannot even start never degrades a Server's health. The actual
/// reachability behaviour against a live socket is exercised in the integration tier, not offline.
/// </summary>
public class UdpNetworkReachabilityProbeTests
{
    [Test]
    [Arguments(null)]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments("not-an-ip")]
    public async Task An_unusable_host_is_unknown_not_unreachable(string? host)
    {
        bool? result = await new UdpNetworkReachabilityProbe()
            .IsUdpPortReachableAsync(host!, 16261, CancellationToken.None);

        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(70000)]
    public async Task An_out_of_range_port_is_unknown_not_unreachable(int port)
    {
        bool? result = await new UdpNetworkReachabilityProbe()
            .IsUdpPortReachableAsync("172.22.0.3", port, CancellationToken.None);

        await Assert.That(result).IsNull();
    }
}
