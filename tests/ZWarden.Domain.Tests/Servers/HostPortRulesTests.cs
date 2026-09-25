using ZWarden.Domain.Servers;

namespace ZWarden.Domain.Tests.Servers;

/// <summary>
/// #229: an operator-chosen host game port. The pair is the port and the port above it, so a game port is valid
/// only if both are unprivileged and addressable; two pairs clash when they share either port.
/// </summary>
public class HostPortRulesTests
{
    [Test]
    [Arguments(1024)]
    [Arguments(16261)]
    [Arguments(27015)]
    [Arguments(65534)]
    public async Task An_unprivileged_game_port_whose_pair_fits_is_valid(int port)
    {
        await Assert.That(HostPortRules.ValidateGamePort(port)).IsNull();
    }

    [Test]
    [Arguments(0)]
    [Arguments(80)]
    [Arguments(1023)]
    [Arguments(65535)]
    [Arguments(70000)]
    [Arguments(-1)]
    public async Task A_privileged_or_unpairable_game_port_is_rejected(int port)
    {
        await Assert.That(HostPortRules.ValidateGamePort(port)).IsNotNull();
    }

    [Test]
    public async Task The_direct_port_is_the_one_above_the_game_port()
    {
        await Assert.That(HostPortRules.DirectPortFor(27015)).IsEqualTo(27016);
    }

    [Test]
    [Arguments(16261, 16261)]
    [Arguments(16261, 16262)]
    [Arguments(16262, 16261)]
    public async Task Pairs_sharing_a_port_overlap(int a, int b)
    {
        await Assert.That(HostPortRules.PairsOverlap(a, b)).IsTrue();
    }

    [Test]
    [Arguments(16261, 16263)]
    [Arguments(16263, 16261)]
    [Arguments(16261, 27015)]
    public async Task Pairs_two_or_more_apart_do_not_overlap(int a, int b)
    {
        await Assert.That(HostPortRules.PairsOverlap(a, b)).IsFalse();
    }
}
