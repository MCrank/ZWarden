using ZWarden.Domain.Setup;

namespace ZWarden.Domain.Tests.Setup;

/// <summary>
/// F33: the installation-wide <see cref="InstallState"/> singleton. It is the first-run gate signal
/// (<see cref="InstallState.IsSetupComplete"/>) and the home of the recorded <see cref="TlsMode"/>.
/// Completion is monotonic — once marked, the first timestamp stands.
/// </summary>
public class InstallStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task A_fresh_install_state_is_not_complete_and_has_no_tls_mode()
    {
        InstallState state = InstallState.CreateDefault();

        await Assert.That(state.Id).IsEqualTo(InstallState.DefaultId);
        await Assert.That(state.IsSetupComplete).IsFalse();
        await Assert.That(state.SetupCompletedAt).IsNull();
        await Assert.That(state.TlsMode).IsNull();
    }

    [Test]
    public async Task Recording_a_tls_mode_stores_it()
    {
        InstallState state = InstallState.CreateDefault();

        state.RecordTlsMode(TlsMode.Private);

        await Assert.That(state.TlsMode).IsEqualTo(TlsMode.Private);
        // Recording a mode does not itself complete setup.
        await Assert.That(state.IsSetupComplete).IsFalse();
    }

    [Test]
    public async Task Recording_a_tls_mode_overwrites_a_previous_one()
    {
        InstallState state = InstallState.CreateDefault();

        state.RecordTlsMode(TlsMode.Public);
        state.RecordTlsMode(TlsMode.ExistingReverseProxy);

        await Assert.That(state.TlsMode).IsEqualTo(TlsMode.ExistingReverseProxy);
    }

    [Test]
    public async Task Marking_complete_sets_the_timestamp_and_is_monotonic()
    {
        InstallState state = InstallState.CreateDefault();

        state.MarkSetupComplete(Now);
        await Assert.That(state.IsSetupComplete).IsTrue();
        await Assert.That(state.SetupCompletedAt).IsEqualTo(Now);

        // A second call does not move the completion instant.
        state.MarkSetupComplete(Now.AddHours(1));
        await Assert.That(state.SetupCompletedAt).IsEqualTo(Now);
    }
}
