using ZWarden.Diagnostics.SupportPackage;

namespace ZWarden.Diagnostics.Tests.SupportPackage;

/// <summary>F30 PR-A: the "Pseudonymize" stage (PRD 53). Consistent within one package, distinct entities get
/// distinct tokens, IPv4 is detected and classified private vs public, and the map is per-instance (D-4).</summary>
public class PseudonymizerTests
{
    private static Pseudonymizer WithTargets(params PseudonymTarget[] targets) => new(targets);

    [Test]
    public async Task A_known_host_is_tokenized()
    {
        Pseudonymizer p = WithTargets(new PseudonymTarget("pz-host-01", PseudonymCategory.Host));

        await Assert.That(p.Apply("running on pz-host-01 now")).IsEqualTo("running on <HOST-1> now");
    }

    [Test]
    public async Task The_same_value_maps_to_the_same_token_across_calls()
    {
        Pseudonymizer p = WithTargets(new PseudonymTarget("Baldspot", PseudonymCategory.Player));

        await Assert.That(p.Apply("Baldspot joined")).IsEqualTo("<PLAYER-1> joined");
        await Assert.That(p.Apply("Baldspot left")).IsEqualTo("<PLAYER-1> left");
    }

    [Test]
    public async Task Distinct_values_get_distinct_numbered_tokens()
    {
        Pseudonymizer p = WithTargets(
            new PseudonymTarget("Baldspot", PseudonymCategory.Player),
            new PseudonymTarget("Kate", PseudonymCategory.Player));

        await Assert.That(p.Apply("Baldspot and Kate")).IsEqualTo("<PLAYER-1> and <PLAYER-2>");
    }

    [Test]
    public async Task A_private_ip_is_classified_private()
    {
        Pseudonymizer p = WithTargets();

        await Assert.That(p.Apply("agent at 192.168.1.50 connected")).IsEqualTo("agent at <PRIVATE-IP-1> connected");
    }

    [Test]
    public async Task A_ten_dot_and_loopback_are_private()
    {
        Pseudonymizer p = WithTargets();

        await Assert.That(p.Apply("10.0.0.5")).IsEqualTo("<PRIVATE-IP-1>");
        await Assert.That(p.Apply("127.0.0.1")).IsEqualTo("<PRIVATE-IP-2>");
    }

    [Test]
    public async Task A_public_ip_is_classified_public()
    {
        Pseudonymizer p = WithTargets();

        await Assert.That(p.Apply("from 203.0.113.7")).IsEqualTo("from <PUBLIC-IP-1>");
    }

    [Test]
    public async Task An_out_of_range_dotted_quad_is_left_alone()
    {
        Pseudonymizer p = WithTargets();

        await Assert.That(p.Apply("version 999.1.1.1 build")).IsEqualTo("version 999.1.1.1 build");
    }

    [Test]
    public async Task Private_and_public_ips_share_no_numbering()
    {
        Pseudonymizer p = WithTargets();

        await Assert.That(p.Apply("192.168.0.1 talks to 8.8.8.8"))
            .IsEqualTo("<PRIVATE-IP-1> talks to <PUBLIC-IP-1>");
    }

    [Test]
    public async Task Two_instances_do_not_share_a_map()
    {
        // D-4: pseudonyms are per-package. Each fresh instance restarts numbering.
        await Assert.That(WithTargets().Apply("8.8.4.4")).IsEqualTo("<PUBLIC-IP-1>");
        await Assert.That(WithTargets().Apply("8.8.4.4")).IsEqualTo("<PUBLIC-IP-1>");
    }

    [Test]
    public async Task Empty_text_is_returned_as_empty()
    {
        await Assert.That(WithTargets().Apply(null)).IsEqualTo("");
    }
}
