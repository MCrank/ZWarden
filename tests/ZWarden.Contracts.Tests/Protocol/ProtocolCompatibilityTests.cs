using ZWarden.Contracts.Protocol;

namespace ZWarden.Contracts.Tests.Protocol;

/// <summary>
/// The version-compatibility rule (F7 test plan 5, ADR 0020): an Agent is compatible iff its
/// protocol version falls within the Web-declared range, inclusive, and a rejection says which
/// bound failed.
/// </summary>
public class ProtocolCompatibilityTests
{
    [Test]
    [Arguments(1, true)]
    [Arguments(2, true)]
    [Arguments(3, true)]
    [Arguments(0, false)]
    [Arguments(4, false)]
    public async Task IsCompatible_accepts_only_versions_within_the_inclusive_range(int agentVersion, bool expected)
    {
        ProtocolVersionRange range = new(MinSupported: 1, Current: 3);

        await Assert.That(ProtocolCompatibility.IsCompatible(agentVersion, range)).IsEqualTo(expected);
    }

    [Test]
    public async Task Negotiate_accepts_an_in_range_version_with_no_reason()
    {
        ProtocolNegotiationResult result = ProtocolCompatibility.Negotiate(2, new ProtocolVersionRange(1, 3));

        await Assert.That(result.IsCompatible).IsTrue();
        await Assert.That(result.Status).IsEqualTo(ProtocolCompatibilityStatus.Compatible);
        await Assert.That(result.RejectionReason).IsNull();
    }

    [Test]
    public async Task Negotiate_rejects_a_below_minimum_version_actionably()
    {
        ProtocolNegotiationResult result = ProtocolCompatibility.Negotiate(0, new ProtocolVersionRange(1, 3));

        await Assert.That(result.IsCompatible).IsFalse();
        await Assert.That(result.Status).IsEqualTo(ProtocolCompatibilityStatus.BelowMinimum);
        await Assert.That(result.RejectionReason).IsNotNull();
        await Assert.That(result.RejectionReason!).Contains("below the minimum");
    }

    [Test]
    public async Task Negotiate_rejects_an_above_current_version_actionably()
    {
        ProtocolNegotiationResult result = ProtocolCompatibility.Negotiate(9, new ProtocolVersionRange(1, 3));

        await Assert.That(result.IsCompatible).IsFalse();
        await Assert.That(result.Status).IsEqualTo(ProtocolCompatibilityStatus.AboveCurrent);
        await Assert.That(result.RejectionReason!).Contains("newer than");
    }

    [Test]
    public async Task The_supported_range_runs_from_one_to_current()
    {
        ProtocolVersionRange range = ProtocolVersionRange.Supported;

        await Assert.That(range.MinSupported).IsEqualTo(1);
        await Assert.That(range.Current).IsEqualTo(ProtocolVersion.Current);
    }
}
