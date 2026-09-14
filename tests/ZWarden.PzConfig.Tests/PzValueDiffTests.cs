using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Internal;
using ZWarden.PzConfig.Model;
using ZWarden.PzConfig.Revisions;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The value-level diff between two configuration documents (F20b, ADR 0011): revisions compare as
/// <em>parsed values</em>, not bytes. A reorder of keys — which PZ performs on every start — is not a
/// change; a value that actually differs is. Named keys compare order-insensitively; positional
/// sequences (spawn entries) compare by index.
/// </summary>
public class PzValueDiffTests
{
    private static IPzConfigDocument Sandbox(string body) =>
        LuaConfigReader.Read(PzConfigKind.SandboxVars, Encoding.UTF8.GetBytes(body)).Document!;

    [Test]
    public async Task Identical_documents_have_no_changes()
    {
        IPzConfigDocument a = Sandbox("SandboxVars = {\n    Zombies = 4,\n    Speed = 2,\n}");
        IPzConfigDocument b = Sandbox("SandboxVars = {\n    Zombies = 4,\n    Speed = 2,\n}");

        await Assert.That(PzValueDiff.Compare(a, b)).IsEmpty();
    }

    [Test]
    public async Task A_reordered_key_is_not_a_change()
    {
        IPzConfigDocument a = Sandbox("SandboxVars = {\n    Zombies = 4,\n    Speed = 2,\n}");
        IPzConfigDocument b = Sandbox("SandboxVars = {\n    Speed = 2,\n    Zombies = 4,\n}");

        await Assert.That(PzValueDiff.Compare(a, b)).IsEmpty();
    }

    [Test]
    public async Task A_changed_scalar_is_reported_with_before_and_after()
    {
        IPzConfigDocument a = Sandbox("SandboxVars = {\n    Zombies = 4,\n}");
        IPzConfigDocument b = Sandbox("SandboxVars = {\n    Zombies = 1,\n}");

        IReadOnlyList<PzConfigChange> changes = PzValueDiff.Compare(a, b);

        await Assert.That(changes.Count).IsEqualTo(1);
        PzConfigChange change = changes[0];
        await Assert.That(change.Path).IsEqualTo("Zombies");
        await Assert.That(change.Kind).IsEqualTo(PzConfigChangeKind.Changed);
        await Assert.That(((PzNumber)change.Before!).Value).IsEqualTo(4d);
        await Assert.That(((PzNumber)change.After!).Value).IsEqualTo(1d);
    }

    [Test]
    public async Task An_added_key_is_reported()
    {
        IPzConfigDocument a = Sandbox("SandboxVars = {\n    Zombies = 4,\n}");
        IPzConfigDocument b = Sandbox("SandboxVars = {\n    Zombies = 4,\n    Speed = 2,\n}");

        IReadOnlyList<PzConfigChange> changes = PzValueDiff.Compare(a, b);

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Path).IsEqualTo("Speed");
        await Assert.That(changes[0].Kind).IsEqualTo(PzConfigChangeKind.Added);
        await Assert.That(changes[0].Before).IsNull();
    }

    [Test]
    public async Task A_removed_key_is_reported()
    {
        IPzConfigDocument a = Sandbox("SandboxVars = {\n    Zombies = 4,\n    Speed = 2,\n}");
        IPzConfigDocument b = Sandbox("SandboxVars = {\n    Zombies = 4,\n}");

        IReadOnlyList<PzConfigChange> changes = PzValueDiff.Compare(a, b);

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Path).IsEqualTo("Speed");
        await Assert.That(changes[0].Kind).IsEqualTo(PzConfigChangeKind.Removed);
        await Assert.That(changes[0].After).IsNull();
    }

    [Test]
    public async Task A_nested_change_has_a_dotted_path()
    {
        IPzConfigDocument a = Sandbox("SandboxVars = {\n    Map = {\n        AllowMiniMap = false,\n    },\n}");
        IPzConfigDocument b = Sandbox("SandboxVars = {\n    Map = {\n        AllowMiniMap = true,\n    },\n}");

        IReadOnlyList<PzConfigChange> changes = PzValueDiff.Compare(a, b);

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Path).IsEqualTo("Map.AllowMiniMap");
    }

    [Test]
    public async Task An_integer_and_a_float_of_the_same_magnitude_differ()
    {
        IPzConfigDocument a = Sandbox("SandboxVars = {\n    XpMultiplier = 1,\n}");
        IPzConfigDocument b = Sandbox("SandboxVars = {\n    XpMultiplier = 1.0,\n}");

        IReadOnlyList<PzConfigChange> changes = PzValueDiff.Compare(a, b);

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Kind).IsEqualTo(PzConfigChangeKind.Changed);
    }

    [Test]
    public async Task A_scalar_becoming_a_table_is_a_change()
    {
        IPzConfigDocument a = Sandbox("SandboxVars = {\n    Thing = 4,\n}");
        IPzConfigDocument b = Sandbox("SandboxVars = {\n    Thing = {\n        Inner = 1,\n    },\n}");

        IReadOnlyList<PzConfigChange> changes = PzValueDiff.Compare(a, b);

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Path).IsEqualTo("Thing");
        await Assert.That(changes[0].Kind).IsEqualTo(PzConfigChangeKind.Changed);
    }

    [Test]
    public async Task Positional_entries_compare_by_index()
    {
        IPzConfigDocument a = LuaConfigReader.Read(PzConfigKind.SpawnRegions,
            Encoding.UTF8.GetBytes("function SpawnRegions()\n\treturn {\n\t\t{ name = \"A\" },\n\t}\nend\n")).Document!;
        IPzConfigDocument b = LuaConfigReader.Read(PzConfigKind.SpawnRegions,
            Encoding.UTF8.GetBytes("function SpawnRegions()\n\treturn {\n\t\t{ name = \"B\" },\n\t}\nend\n")).Document!;

        IReadOnlyList<PzConfigChange> changes = PzValueDiff.Compare(a, b);

        await Assert.That(changes.Count).IsEqualTo(1);
        await Assert.That(changes[0].Path).IsEqualTo("[0].name");
        await Assert.That(changes[0].Kind).IsEqualTo(PzConfigChangeKind.Changed);
    }
}
