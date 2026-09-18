using ZWarden.PzConfig.Validation;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The schema carries operator-facing display metadata — a friendly <see cref="PzSchemaEntry.Label"/>,
/// a grouping <see cref="PzSchemaEntry.Section"/>, and an optional authored <see cref="PzSchemaEntry.Description"/>
/// that overrides the file comment — so a config editor can group settings and label them without the
/// operator reading dotted paths (F20c, PR-A slice 3). Metadata is additive: it never affects
/// validation, and an unknown key still has no entry.
/// </summary>
public class PzSchemaMetadataTests
{
    [Test]
    public async Task Display_sets_label_section_and_description_non_destructively()
    {
        PzSchemaEntry bare = PzSchemaEntry.Whole("Zombies", min: 1, max: 6, @default: 4);
        PzSchemaEntry shown = bare.Display("Zombies", "Population", "Overall zombie population.");

        await Assert.That(bare.Label).IsNull();
        await Assert.That(shown.Label).IsEqualTo("Population");
        await Assert.That(shown.Section).IsEqualTo("Zombies");
        await Assert.That(shown.Description).IsEqualTo("Overall zombie population.");
        // Type/range/default carry through untouched.
        await Assert.That(shown.Max).IsEqualTo(6d);
    }

    [Test]
    public async Task Sandbox_schema_groups_and_labels_known_keys()
    {
        await Assert.That(PzSchema.SandboxVars.TryGet("Zombies", out PzSchemaEntry zombies)).IsTrue();
        await Assert.That(zombies.Section).IsEqualTo("Zombies");
        await Assert.That(zombies.Label).IsEqualTo("Population");

        await Assert.That(PzSchema.SandboxVars.TryGet("Map.AllowMiniMap", out PzSchemaEntry miniMap)).IsTrue();
        await Assert.That(miniMap.Section).IsEqualTo("World & Map");
        await Assert.That(miniMap.Label).IsEqualTo("Allow mini-map");
    }

    [Test]
    public async Task Ini_schema_groups_and_labels_known_keys()
    {
        await Assert.That(PzSchema.Ini.TryGet("MaxPlayers", out PzSchemaEntry maxPlayers)).IsTrue();
        await Assert.That(maxPlayers.Section).IsEqualTo("Access");
        await Assert.That(maxPlayers.Label).IsEqualTo("Max players");

        await Assert.That(PzSchema.Ini.TryGet("RCONPort", out PzSchemaEntry rcon)).IsTrue();
        await Assert.That(rcon.Section).IsEqualTo("Networking");
    }

    [Test]
    public async Task Newly_grouped_sandbox_keys_are_present_with_ranges()
    {
        await Assert.That(PzSchema.SandboxVars.TryGet("DayLength", out PzSchemaEntry day)).IsTrue();
        await Assert.That(day.Type).IsEqualTo(PzValueType.Whole);

        await Assert.That(PzSchema.SandboxVars.TryGet("MultiplierConfig.Glassmaking", out PzSchemaEntry glass)).IsTrue();
        await Assert.That(glass.Type).IsEqualTo(PzValueType.Number);
        await Assert.That(glass.Section).IsEqualTo("Multipliers");
    }

    [Test]
    public async Task An_unknown_key_still_has_no_schema_entry()
    {
        await Assert.That(PzSchema.SandboxVars.TryGet("SomeModAddedKey", out _)).IsFalse();
    }
}
