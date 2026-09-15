using Microsoft.AspNetCore.SignalR.Client;
using ZWarden.Agent.ControlPlane;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>
/// F35 WAN reconnect behaviour: a remote Agent reaches the control plane over the open Internet, where drops
/// are longer and more frequent than on a LAN. The reconnect policy must therefore <b>retry forever</b> (never
/// give up the way SignalR's built-in policy does after ~30s) with an <b>exponential backoff capped</b> so a
/// long outage does not become a busy-loop against the control plane.
/// </summary>
public class CappedBackoffRetryPolicyTests
{
    private static readonly CappedBackoffRetryPolicy Policy = new();

    private static TimeSpan? Delay(long previousRetryCount) =>
        Policy.NextRetryDelay(new RetryContext { PreviousRetryCount = previousRetryCount });

    [Test]
    public async Task It_never_gives_up_so_a_remote_agent_keeps_trying_across_a_long_wan_outage()
    {
        // A null delay is how an IRetryPolicy says "stop reconnecting" — this policy must never say that.
        await Assert.That(Delay(0)).IsNotNull();
        await Assert.That(Delay(50)).IsNotNull();
        await Assert.That(Delay(100_000)).IsNotNull();
    }

    [Test]
    public async Task It_backs_off_exponentially_from_one_second()
    {
        await Assert.That(Delay(0)).IsEqualTo(TimeSpan.FromSeconds(1));
        await Assert.That(Delay(1)).IsEqualTo(TimeSpan.FromSeconds(2));
        await Assert.That(Delay(2)).IsEqualTo(TimeSpan.FromSeconds(4));
        await Assert.That(Delay(3)).IsEqualTo(TimeSpan.FromSeconds(8));
    }

    [Test]
    public async Task It_caps_the_backoff_at_thirty_seconds()
    {
        // Once the exponential term passes 30s it must clamp — a busy remote host never hammers the control
        // plane faster than every 30s, no matter how long it has been offline.
        await Assert.That(Delay(5)).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(Delay(9)).IsEqualTo(TimeSpan.FromSeconds(30));
        await Assert.That(Delay(1_000)).IsEqualTo(TimeSpan.FromSeconds(30));
    }
}
