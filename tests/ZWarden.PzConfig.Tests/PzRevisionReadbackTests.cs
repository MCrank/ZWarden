using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Internal;
using ZWarden.PzConfig.Model;
using ZWarden.PzConfig.Revisions;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// F20b PR-4: reading a persisted revision back. A <see cref="ConfigurationRevision"/> stores only the
/// order-normalized canonical snapshot text (values, never bytes — ADR 0011); the UI has no live file to
/// re-parse. <see cref="PzValueSnapshot.Parse"/> reconstructs a snapshot from that stored text so the
/// history diff and a value-level restore can run against two persisted revisions, and
/// <see cref="PzValueDiff.Compare(PzValueSnapshot, PzValueSnapshot)"/> diffs two snapshots directly.
/// Neither path touches Loretta.
/// </summary>
public class PzRevisionReadbackTests
{
    private static IPzConfigDocument Sandbox(string body) =>
        LuaConfigReader.Read(PzConfigKind.SandboxVars, Encoding.UTF8.GetBytes(body)).Document!;

    [Test]
    public async Task Parse_round_trips_the_canonical_text_and_hash()
    {
        PzValueSnapshot original = PzValueSnapshot.Of(Sandbox(
            "SandboxVars = {\n    Zombies = 4,\n    Speed = 2.5,\n    Map = {\n        AllowMiniMap = false,\n    },\n    Name = \"Bob's server\",\n}"));

        PzValueSnapshot parsed = PzValueSnapshot.Parse(original.CanonicalText);

        await Assert.That(parsed.CanonicalText).IsEqualTo(original.CanonicalText);
        await Assert.That(parsed.Hash).IsEqualTo(original.Hash);
        await Assert.That(parsed.Scalars.Count).IsEqualTo(original.Scalars.Count);
    }

    [Test]
    public async Task Parse_reconstructs_each_scalar_kind()
    {
        PzValueSnapshot original = PzValueSnapshot.Of(Sandbox(
            "SandboxVars = {\n    Flag = true,\n    Count = 6,\n    Ratio = 1.0,\n    Title = \"hi\",\n}"));

        PzValueSnapshot parsed = PzValueSnapshot.Parse(original.CanonicalText);
        Dictionary<string, PzValue> byPath = parsed.Scalars.ToDictionary(s => s.Path, s => s.Value, StringComparer.Ordinal);

        await Assert.That(((PzBoolean)byPath["Flag"]).Value).IsTrue();
        PzNumber count = (PzNumber)byPath["Count"];
        await Assert.That(count.Value).IsEqualTo(6d);
        await Assert.That(count.IsInteger).IsTrue();
        await Assert.That(count.Lexeme).IsEqualTo("6");
        PzNumber ratio = (PzNumber)byPath["Ratio"];
        await Assert.That(ratio.IsInteger).IsFalse();
        await Assert.That(ratio.Lexeme).IsEqualTo("1.0");
        await Assert.That(((PzString)byPath["Title"]).Value).IsEqualTo("hi");
    }

    [Test]
    public async Task Parse_preserves_a_string_carrying_separator_characters()
    {
        PzValueSnapshot original = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Motd = \"a:b\\nc\",\n}"));

        PzValueSnapshot parsed = PzValueSnapshot.Parse(original.CanonicalText);

        await Assert.That(((PzString)parsed.Scalars[0].Value).Value).IsEqualTo("a:b\nc");
    }

    [Test]
    public async Task Parse_preserves_a_negative_number()
    {
        PzValueSnapshot original = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Offset = -3,\n}"));

        PzValueSnapshot parsed = PzValueSnapshot.Parse(original.CanonicalText);
        PzNumber offset = (PzNumber)parsed.Scalars[0].Value;

        await Assert.That(offset.Value).IsEqualTo(-3d);
        await Assert.That(offset.IsInteger).IsTrue();
        await Assert.That(offset.Lexeme).IsEqualTo("-3");
    }

    [Test]
    public async Task Parse_rejects_malformed_text()
    {
        await Assert.That(() => PzValueSnapshot.Parse("not json")).Throws<FormatException>();
    }

    [Test]
    public async Task Parse_rejects_an_unknown_scalar_encoding()
    {
        // A well-formed JSON pair whose encoding tag is not one of b:/n:/s:.
        await Assert.That(() => PzValueSnapshot.Parse("[[\"Zombies\",\"x:4\"]]")).Throws<FormatException>();
    }

    [Test]
    public async Task A_parsed_snapshot_restores_against_a_live_document()
    {
        // The whole point of Parse: PzRestore can plan from a persisted snapshot to the live file.
        PzValueSnapshot target = PzValueSnapshot.Parse(
            PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n}")).CanonicalText);
        IPzConfigDocument current = Sandbox("SandboxVars = {\n    Zombies = 1,\n}");

        PzRestorePlan plan = PzRestore.PlanTo(target, current);

        await Assert.That(plan.Edits.Count).IsEqualTo(1);
        await Assert.That(plan.Edits[0].Path).IsEqualTo("Zombies");
        await Assert.That(((PzNumber)plan.Edits[0].Value).Value).IsEqualTo(4d);
    }

    [Test]
    public async Task Compare_reports_a_changed_value_between_two_snapshots()
    {
        PzValueSnapshot before = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n}"));
        PzValueSnapshot after = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 1,\n}"));

        IReadOnlyList<PzConfigChange> changes = PzValueDiff.Compare(before, after);

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Path).IsEqualTo("Zombies");
        await Assert.That(changes[0].Kind).IsEqualTo(PzConfigChangeKind.Changed);
        await Assert.That(((PzNumber)changes[0].Before!).Value).IsEqualTo(4d);
        await Assert.That(((PzNumber)changes[0].After!).Value).IsEqualTo(1d);
    }

    [Test]
    public async Task Compare_reports_added_and_removed_paths()
    {
        PzValueSnapshot before = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n    Gone = 2,\n}"));
        PzValueSnapshot after = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n    Fresh = 9,\n}"));

        IReadOnlyList<PzConfigChange> changes = PzValueDiff.Compare(before, after);

        await Assert.That(changes.Select(c => (c.Path, c.Kind))).Contains(("Fresh", PzConfigChangeKind.Added));
        await Assert.That(changes.Select(c => (c.Path, c.Kind))).Contains(("Gone", PzConfigChangeKind.Removed));
        await Assert.That(changes.Any(c => c.Path == "Zombies")).IsFalse();
    }

    [Test]
    public async Task Compare_is_reorder_stable()
    {
        PzValueSnapshot before = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n    Speed = 2,\n}"));
        PzValueSnapshot after = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Speed = 2,\n    Zombies = 4,\n}"));

        await Assert.That(PzValueDiff.Compare(before, after)).IsEmpty();
    }

    [Test]
    public async Task Compare_treats_an_integer_and_a_float_as_changed()
    {
        PzValueSnapshot before = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    XpMultiplier = 1,\n}"));
        PzValueSnapshot after = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    XpMultiplier = 1.0,\n}"));

        IReadOnlyList<PzConfigChange> changes = PzValueDiff.Compare(before, after);

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Kind).IsEqualTo(PzConfigChangeKind.Changed);
    }
}
