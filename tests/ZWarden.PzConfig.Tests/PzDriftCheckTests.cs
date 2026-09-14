using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Internal;
using ZWarden.PzConfig.Revisions;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The value-level drift check (F20b, ADR 0011): before any write, the live file is re-parsed, canonicalized,
/// and its hash compared against the last recorded revision's baseline. A match is <see cref="PzDriftStatus.InSync"/>
/// and the write proceeds; a mismatch is <see cref="PzDriftStatus.Drifted"/> and the write is <b>refused</b>
/// (fail closed), because a second author — the in-game admin panel, the settings editor, or the server itself —
/// changed the file behind ZWarden's back. A reorder is not drift (the canonical hash normalizes order).
/// </summary>
public class PzDriftCheckTests
{
    private static IPzConfigDocument Sandbox(string body) =>
        LuaConfigReader.Read(PzConfigKind.SandboxVars, Encoding.UTF8.GetBytes(body)).Document!;

    private static string HashOf(IPzConfigDocument document) => PzValueSnapshot.Of(document).Hash;

    [Test]
    public async Task An_unchanged_file_is_in_sync_and_the_write_is_allowed()
    {
        IPzConfigDocument live = Sandbox("SandboxVars = {\n    Zombies = 4,\n}");
        string baseline = HashOf(live);

        PzDriftResult result = PzDriftCheck.Compare(baseline, live);

        await Assert.That(result.Status).IsEqualTo(PzDriftStatus.InSync);
        await Assert.That(result.IsDrifted).IsFalse();
        await Assert.That(result.WriteAllowed).IsTrue();
        await Assert.That(result.CurrentHash).IsEqualTo(baseline);
        await Assert.That(result.BaselineHash).IsEqualTo(baseline);
    }

    [Test]
    public async Task A_reorder_is_not_drift()
    {
        string baseline = HashOf(Sandbox("SandboxVars = {\n    Zombies = 4,\n    Speed = 2,\n}"));
        IPzConfigDocument live = Sandbox("SandboxVars = {\n    Speed = 2,\n    Zombies = 4,\n}");

        PzDriftResult result = PzDriftCheck.Compare(baseline, live);

        await Assert.That(result.Status).IsEqualTo(PzDriftStatus.InSync);
    }

    [Test]
    public async Task An_out_of_band_value_change_is_drift_and_the_write_is_refused()
    {
        string baseline = HashOf(Sandbox("SandboxVars = {\n    Zombies = 4,\n}"));
        IPzConfigDocument live = Sandbox("SandboxVars = {\n    Zombies = 1,\n}");

        PzDriftResult result = PzDriftCheck.Compare(baseline, live);

        await Assert.That(result.Status).IsEqualTo(PzDriftStatus.Drifted);
        await Assert.That(result.IsDrifted).IsTrue();
        await Assert.That(result.WriteAllowed).IsFalse();
        await Assert.That(result.CurrentHash).IsNotEqualTo(baseline);
        await Assert.That(result.BaselineHash).IsEqualTo(baseline);
    }

    [Test]
    public async Task No_baseline_allows_the_write_without_asserting_sync()
    {
        IPzConfigDocument live = Sandbox("SandboxVars = {\n    Zombies = 4,\n}");

        PzDriftResult result = PzDriftCheck.Compare(null, live);

        await Assert.That(result.Status).IsEqualTo(PzDriftStatus.NoBaseline);
        await Assert.That(result.WriteAllowed).IsTrue();
        await Assert.That(result.IsDrifted).IsFalse();
        await Assert.That(result.BaselineHash).IsNull();
        await Assert.That(result.CurrentHash).IsEqualTo(HashOf(live));
    }

    [Test]
    public async Task A_blank_baseline_is_treated_as_no_baseline()
    {
        PzDriftResult result = PzDriftCheck.Compare("   ", Sandbox("SandboxVars = {\n    Zombies = 4,\n}"));

        await Assert.That(result.Status).IsEqualTo(PzDriftStatus.NoBaseline);
        await Assert.That(result.WriteAllowed).IsTrue();
    }

    [Test]
    public async Task Comparing_against_a_precomputed_snapshot_agrees_with_the_document_overload()
    {
        IPzConfigDocument live = Sandbox("SandboxVars = {\n    Zombies = 4,\n}");
        string baseline = HashOf(live);

        PzValueSnapshot snapshot = PzValueSnapshot.Of(live);
        PzDriftResult viaSnapshot = PzDriftCheck.Compare(baseline, snapshot);
        PzDriftResult viaDocument = PzDriftCheck.Compare(baseline, live);

        await Assert.That(viaSnapshot.Status).IsEqualTo(PzDriftStatus.InSync);
        await Assert.That(viaSnapshot.CurrentHash).IsEqualTo(viaDocument.CurrentHash);
    }
}
