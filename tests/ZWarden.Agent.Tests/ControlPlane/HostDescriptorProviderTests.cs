using ZWarden.Agent.ControlPlane;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>
/// F35 D-1: the Agent self-describes its Host (hostname, agent version, OS platform) on the
/// <c>AgentHello</c> so the operator inventory can tell Hosts apart. The values are environment-derived;
/// the invariant that matters is they are always present (never blank — a blank would be rejected by the
/// entity and tear the handshake) and the platform label is one of the known set.
/// </summary>
public class HostDescriptorProviderTests
{
    [Test]
    public async Task Current_reports_a_non_blank_hostname_version_and_platform()
    {
        var host = HostDescriptorProvider.Current;

        await Assert.That(string.IsNullOrWhiteSpace(host.Hostname)).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(host.AgentVersion)).IsFalse();
        await Assert.That(string.IsNullOrWhiteSpace(host.OsPlatform)).IsFalse();
    }

    [Test]
    public async Task OsPlatformLabel_is_one_of_the_known_labels()
    {
        string[] known = ["Linux", "Windows", "macOS", "FreeBSD", "Unknown"];

        await Assert.That(known).Contains(HostDescriptorProvider.Current.OsPlatform);
    }
}
