using ZWarden.PzConfig.Validation;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// #227: the setting catalog files every key into an operator-facing section and gives it a readable label, so the
/// config editor is not one long "Other" list of raw paths. Vanilla keys follow the in-game server-settings screen's
/// grouping; a key the catalog has never seen falls back to a name-prefix rule, a mod's nested sandbox table becomes
/// its own section, and only a truly unmatched key lands in "Other". Proven against the full-key B42 fixtures.
/// </summary>
public class PzSettingCatalogTests
{
    [Test]
    public async Task Every_vanilla_ini_key_in_a_full_b42_file_has_a_section()
    {
        IReadOnlyList<string> paths = B42Fixtures.PathsOf(B42Fixtures.ReadIni());

        string[] unclassified = [.. paths.Where(p => PzSettingCatalog.SectionOf(PzConfigKind.Ini, p) == PzSettingCatalog.OtherSection)];

        await Assert.That(paths.Count).IsGreaterThanOrEqualTo(140);
        await Assert.That(unclassified).IsEmpty();
    }

    [Test]
    public async Task Every_sandbox_key_in_a_full_b42_file_has_a_section()
    {
        IReadOnlyList<string> paths = B42Fixtures.PathsOf(B42Fixtures.ReadSandboxVars());

        string[] unclassified = [.. paths.Where(p => PzSettingCatalog.SectionOf(PzConfigKind.SandboxVars, p) == PzSettingCatalog.OtherSection)];

        await Assert.That(paths.Count).IsGreaterThanOrEqualTo(270);
        await Assert.That(unclassified).IsEmpty();
    }

    [Test]
    [Arguments("AdminSafehouse", "Safehouse")]
    [Arguments("RCONPort", "RCON")]
    [Arguments("AntiCheatSpeed", "Anti-cheat")]
    [Arguments("MaxPlayers", "Players")]
    [Arguments("PVP", "PVP")]
    [Arguments("DoLuaChecksum", "General")]
    [Arguments("Mods", "Mods & map")]
    public async Task Ini_keys_follow_the_in_game_grouping(string path, string section)
    {
        await Assert.That(PzSettingCatalog.SectionOf(PzConfigKind.Ini, path)).IsEqualTo(section);
    }

    [Test]
    [Arguments("Zombies", "Zombies")]
    [Arguments("ZombieLore.Speed", "Zombie lore")]
    [Arguments("ZombieConfig.RallyGroupSize", "Advanced zombies")]
    [Arguments("MultiplierConfig.Glassmaking", "XP multipliers")]
    [Arguments("Map.AllowMiniMap", "Map")]
    [Arguments("Basement.SpawnFrequency", "Basements")]
    [Arguments("FoodLootNew", "Loot rarity")]
    [Arguments("DayLength", "Time")]
    public async Task Sandbox_keys_follow_the_in_game_grouping(string path, string section)
    {
        await Assert.That(PzSettingCatalog.SectionOf(PzConfigKind.SandboxVars, path)).IsEqualTo(section);
    }

    [Test]
    [Arguments("SafehouseNewPatchKey", "Safehouse")]
    [Arguments("AntiCheatSomethingNew", "Anti-cheat")]
    [Arguments("DiscordNewChannel", "Discord")]
    [Arguments("VoiceNewOption", "Voice")]
    public async Task An_unseen_ini_key_falls_back_to_its_name_prefix(string path, string section)
    {
        await Assert.That(PzSettingCatalog.SectionOf(PzConfigKind.Ini, path)).IsEqualTo(section);
    }

    [Test]
    public async Task A_mods_nested_sandbox_table_becomes_its_own_section()
    {
        await Assert.That(PzSettingCatalog.SectionOf(PzConfigKind.SandboxVars, "BetterLockpicking.Difficulty"))
            .IsEqualTo("Better lockpicking");

        // An unseen key under a vanilla table still lands in that table's section.
        await Assert.That(PzSettingCatalog.SectionOf(PzConfigKind.SandboxVars, "ZombieLore.NewPatchKey"))
            .IsEqualTo("Zombie lore");
    }

    [Test]
    public async Task A_truly_unmatched_key_is_other()
    {
        await Assert.That(PzSettingCatalog.SectionOf(PzConfigKind.Ini, "Zzyzx")).IsEqualTo(PzSettingCatalog.OtherSection);
        await Assert.That(PzSettingCatalog.SectionOf(PzConfigKind.SandboxVars, "Zzyzx")).IsEqualTo(PzSettingCatalog.OtherSection);
    }

    [Test]
    public async Task Sections_rank_vanilla_first_then_mod_sections_then_other()
    {
        int players = PzSettingCatalog.SectionRank(PzConfigKind.Ini, "Players");
        int details = PzSettingCatalog.SectionRank(PzConfigKind.Ini, "Details");
        int zombies = PzSettingCatalog.SectionRank(PzConfigKind.SandboxVars, "Zombies");
        int mod = PzSettingCatalog.SectionRank(PzConfigKind.SandboxVars, "Better lockpicking");
        int other = PzSettingCatalog.SectionRank(PzConfigKind.SandboxVars, PzSettingCatalog.OtherSection);

        await Assert.That(details).IsLessThan(players);
        await Assert.That(zombies).IsLessThan(mod);
        await Assert.That(mod).IsLessThan(other);
    }

    [Test]
    [Arguments("AdminSafehouse", "Admin safehouse")]
    [Arguments("RCONPort", "RCON port")]
    [Arguments("UDPPort", "UDP port")]
    [Arguments("PVP", "PVP")]
    [Arguments("Voice3D", "Voice 3D")]
    [Arguments("FoodLootNew", "Food loot")]
    [Arguments("ServerPlayerID", "Server player ID")]
    [Arguments("server_browser_announced_ip", "Server browser announced IP")]
    [Arguments("ZombieLore.ZombiesDragDown", "Zombies drag down")]
    [Arguments("MultiplierConfig.Glassmaking", "Glassmaking")]
    [Arguments("VERSION", "VERSION")]
    public async Task Labels_humanize_the_last_path_segment(string path, string label)
    {
        await Assert.That(PzSettingCatalog.LabelOf(path)).IsEqualTo(label);
    }

    [Test]
    public async Task Schema_sections_agree_with_the_catalog()
    {
        // One source of truth: a schema entry that sets a section must not split a catalog section in two.
        foreach ((PzConfigKind kind, PzSchema schema) in new[] { (PzConfigKind.Ini, PzSchema.Ini), (PzConfigKind.SandboxVars, PzSchema.SandboxVars) })
        {
            foreach (PzSchemaEntry entry in schema.Entries.Where(e => e.Section is not null))
            {
                await Assert.That(entry.Section).IsEqualTo(PzSettingCatalog.SectionOf(kind, entry.Path));
            }
        }
    }
}
