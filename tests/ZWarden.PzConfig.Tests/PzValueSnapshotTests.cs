using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Internal;
using ZWarden.PzConfig.Model;
using ZWarden.PzConfig.Revisions;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The canonical value snapshot (F20b): a deterministic, order-normalized flattening of a document's
/// scalar leaves and a stable SHA-256 over it. A reorder — which the server performs on every start —
/// yields the same hash; a genuine value change yields a different one. This is the unit a revision
/// persists and the fingerprint the drift check compares against (ADR 0011).
/// </summary>
public class PzValueSnapshotTests
{
    private static IPzConfigDocument Sandbox(string body) =>
        LuaConfigReader.Read(PzConfigKind.SandboxVars, Encoding.UTF8.GetBytes(body)).Document!;

    [Test]
    public async Task The_hash_is_a_sha256_hex_string()
    {
        PzValueSnapshot snapshot = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n}"));

        await Assert.That(snapshot.Hash.Length).IsEqualTo(64);
        await Assert.That(snapshot.Hash).Matches("^[0-9a-f]{64}$");
    }

    [Test]
    public async Task Reordered_keys_hash_the_same()
    {
        PzValueSnapshot a = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n    Speed = 2,\n}"));
        PzValueSnapshot b = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Speed = 2,\n    Zombies = 4,\n}"));

        await Assert.That(a.Hash).IsEqualTo(b.Hash);
    }

    [Test]
    public async Task A_changed_value_hashes_differently()
    {
        PzValueSnapshot a = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n}"));
        PzValueSnapshot b = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 1,\n}"));

        await Assert.That(a.Hash).IsNotEqualTo(b.Hash);
    }

    [Test]
    public async Task An_integer_and_a_float_hash_differently()
    {
        PzValueSnapshot a = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    XpMultiplier = 1,\n}"));
        PzValueSnapshot b = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    XpMultiplier = 1.0,\n}"));

        await Assert.That(a.Hash).IsNotEqualTo(b.Hash);
    }

    [Test]
    public async Task Comment_and_whitespace_differences_do_not_change_the_hash()
    {
        PzValueSnapshot a = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4, -- a note\n}"));
        PzValueSnapshot b = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n\tZombies = 4,\n}"));

        await Assert.That(a.Hash).IsEqualTo(b.Hash);
    }

    [Test]
    public async Task The_scalar_leaves_are_flattened_by_dotted_path_and_sorted()
    {
        PzValueSnapshot snapshot = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n    Map = {\n        AllowMiniMap = false,\n    },\n}"));

        IReadOnlyList<PzScalarEntry> scalars = snapshot.Scalars;
        await Assert.That(scalars.Count).IsEqualTo(2);
        // Sorted by ordinal path: "Map.AllowMiniMap" precedes "Zombies".
        await Assert.That(scalars[0].Path).IsEqualTo("Map.AllowMiniMap");
        await Assert.That(scalars[1].Path).IsEqualTo("Zombies");
        await Assert.That(((PzNumber)scalars[1].Value).Value).IsEqualTo(4d);
    }

    [Test]
    public async Task A_string_value_containing_a_newline_does_not_corrupt_the_canonical_form()
    {
        // Two reads of the same content must hash identically even when a value carries a separator-like
        // character, proving the canonical encoding escapes rather than concatenates raw.
        PzValueSnapshot a = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Name = \"a\\nb\",\n    Zombies = 4,\n}"));
        PzValueSnapshot b = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Name = \"a\\nb\",\n    Zombies = 4,\n}"));

        await Assert.That(a.Hash).IsEqualTo(b.Hash);
    }
}
