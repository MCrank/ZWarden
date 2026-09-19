using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Mods;
using ZWarden.Agent.SteamCmd;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;
using ZWarden.PzConfig;

namespace ZWarden.Agent.Tests.Mods;

/// <summary>
/// F21: the discovery engine, end to end against a real temp filesystem and the real F20a parser (no Docker, no
/// network). It walks <c>&lt;installVolume&gt;/steamapps/workshop/content/108600/&lt;id&gt;/mods/&lt;folder&gt;/mod.info</c>,
/// reads <c>WorkshopItems=</c>/<c>Mods=</c> from the data-volume <c>servertest.ini</c>, and returns the mapping and
/// findings.
/// </summary>
public class ModDiscoveryTests
{
    private static (ModDiscovery Discovery, string Root, ServerId ServerId) NewDiscovery()
    {
        string root = Path.Combine(Path.GetTempPath(), "zw-f21-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        AgentOptions options = new() { DataMountRoot = root };
        ModDiscovery discovery = new(
            new ServerInstallPaths(Options.Create(options)),
            new PzConfigParser(),
            Options.Create(options),
            NullLogger<ModDiscovery>.Instance);
        return (discovery, root, ServerId.New());
    }

    private static void WriteMod(string root, ServerId serverId, string workshopId, string modFolder, string modInfo)
    {
        string dir = Path.Combine(root, $"{serverId}.server", "steamapps", "workshop", "content", "108600", workshopId, "mods", modFolder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mod.info"), modInfo);
    }

    private static void WriteIni(string root, ServerId serverId, string content)
    {
        string dir = Path.Combine(root, serverId.ToString(), "Server");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "servertest.ini"), Encoding.UTF8.GetBytes(content));
    }

    [Test]
    public async Task Discovers_installed_items_and_maps_workshop_to_mods()
    {
        (ModDiscovery discovery, string root, ServerId serverId) = NewDiscovery();
        try
        {
            WriteMod(root, serverId, "2392709985", "Brita", "name=Brita's Weapon Pack\nid=Brita_2\n");
            WriteMod(root, serverId, "2335368829", "ModA", "name=Mod A\nid=ModA\n");
            WriteMod(root, serverId, "2335368829", "ModB", "id=ModB\n");
            WriteIni(root, serverId, "WorkshopItems=2392709985;2335368829\nMods=Brita_2;ModA;ModB\n");

            ModDiscoveryResult result = await discovery.DiscoverAsync(serverId, CancellationToken.None);

            await Assert.That(result.InstalledItems.Count).IsEqualTo(2);
            DiscoveredWorkshopItem brita = result.InstalledItems.Single(i => i.WorkshopId == "2392709985");
            await Assert.That(brita.Mods.Single().ModId).IsEqualTo("Brita_2");
            await Assert.That(brita.Mods.Single().Name).IsEqualTo("Brita's Weapon Pack");

            DiscoveredWorkshopItem multi = result.InstalledItems.Single(i => i.WorkshopId == "2335368829");
            string[] multiMods = ["ModA", "ModB"];
            string[] configured = ["2392709985", "2335368829"];
            string[] enabled = ["Brita_2", "ModA", "ModB"];
            await Assert.That(multi.Mods.Select(m => m.ModId)).IsEquivalentTo(multiMods);

            await Assert.That(result.ConfiguredWorkshopIds).IsEquivalentTo(configured);
            await Assert.That(result.EnabledModIds).IsEquivalentTo(enabled);
            await Assert.That(result.Findings).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Reports_findings_when_disk_and_config_disagree()
    {
        (ModDiscovery discovery, string root, ServerId serverId) = NewDiscovery();
        try
        {
            WriteMod(root, serverId, "111", "OnDisk", "id=OnDisk\n");
            // Config references a workshop id that is not on disk and enables a mod nothing provides; OnDisk is
            // installed but not enabled.
            WriteIni(root, serverId, "WorkshopItems=111;999\nMods=Ghost\n");

            ModDiscoveryResult result = await discovery.DiscoverAsync(serverId, CancellationToken.None);

            await Assert.That(result.Findings.Any(f => f.Kind == ModCompatKind.ReferencedNotInstalled && f.Subject == "999")).IsTrue();
            await Assert.That(result.Findings.Any(f => f.Kind == ModCompatKind.EnabledButMissing && f.Subject == "Ghost")).IsTrue();
            await Assert.That(result.Findings.Any(f => f.Kind == ModCompatKind.InstalledButInactive && f.Subject == "OnDisk")).IsTrue();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task An_absent_workshop_tree_yields_no_installed_items()
    {
        (ModDiscovery discovery, string root, ServerId serverId) = NewDiscovery();
        try
        {
            WriteIni(root, serverId, "WorkshopItems=\nMods=\n");

            ModDiscoveryResult result = await discovery.DiscoverAsync(serverId, CancellationToken.None);

            await Assert.That(result.InstalledItems).IsEmpty();
            await Assert.That(result.ConfiguredWorkshopIds).IsEmpty();
            await Assert.That(result.EnabledModIds).IsEmpty();
            await Assert.That(result.Findings).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task An_absent_config_still_reports_disk_facts()
    {
        (ModDiscovery discovery, string root, ServerId serverId) = NewDiscovery();
        try
        {
            WriteMod(root, serverId, "111", "OnDisk", "id=OnDisk\n");

            ModDiscoveryResult result = await discovery.DiscoverAsync(serverId, CancellationToken.None);

            await Assert.That(result.InstalledItems.Single().WorkshopId).IsEqualTo("111");
            await Assert.That(result.ConfiguredWorkshopIds).IsEmpty();
            await Assert.That(result.EnabledModIds).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task A_build_42_only_mod_is_discovered_from_its_version_folder()
    {
        (ModDiscovery discovery, string root, ServerId serverId) = NewDiscovery();
        try
        {
            // A B42-only mod ships no root mod.info, only <modFolder>/42/mod.info (research §6).
            string b42Dir = Path.Combine(
                root, $"{serverId}.server", "steamapps", "workshop", "content", "108600", "555", "mods", "NewMod", "42");
            Directory.CreateDirectory(b42Dir);
            File.WriteAllText(Path.Combine(b42Dir, "mod.info"), "name=New Mod\nid=NewMod42\n");
            WriteIni(root, serverId, "WorkshopItems=555\nMods=NewMod42\n");

            ModDiscoveryResult result = await discovery.DiscoverAsync(serverId, CancellationToken.None);

            DiscoveredMod mod = result.InstalledItems.Single(i => i.WorkshopId == "555").Mods.Single();
            await Assert.That(mod.ModId).IsEqualTo("NewMod42");
            await Assert.That(result.Findings).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Carries_the_mod_info_version_and_dependency_metadata()
    {
        (ModDiscovery discovery, string root, ServerId serverId) = NewDiscovery();
        try
        {
            WriteMod(root, serverId, "111", "AZ",
                "id=AuthenticZ\nname=Authentic Z\nversion=42\npzversion=41\nversionMin=41.78\nrequire=DepMod\nincompatible=\\Lite\ntags=Realistic,Overhaul\n");
            WriteIni(root, serverId, "WorkshopItems=111\nMods=AuthenticZ\n");

            ModDiscoveryResult result = await discovery.DiscoverAsync(serverId, CancellationToken.None);

            DiscoveredMod mod = result.InstalledItems.Single().Mods.Single();
            await Assert.That(mod.Version).IsEqualTo("42");
            await Assert.That(mod.PzVersion).IsEqualTo("41");
            await Assert.That(mod.VersionMin).IsEqualTo("41.78");
            string[] requires = ["DepMod"];
            string[] incompatible = ["\\Lite"];
            string[] tags = ["Realistic", "Overhaul"];
            await Assert.That(mod.Requires).IsEquivalentTo(requires);
            await Assert.That(mod.Incompatible).IsEquivalentTo(incompatible);
            await Assert.That(mod.Tags).IsEquivalentTo(tags);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task Prefers_the_build_42_mod_info_metadata_when_both_are_present()
    {
        (ModDiscovery discovery, string root, ServerId serverId) = NewDiscovery();
        try
        {
            // A dual-build mod ships a root (B41) mod.info and a 42/ (B42) one whose metadata differs (research §6).
            // B42 is the current default build, so its version folder is the version-appropriate source.
            WriteMod(root, serverId, "111", "AZ", "id=AuthenticZ\nname=Authentic Z\npzversion=41\n");
            string b42Dir = Path.Combine(
                root, $"{serverId}.server", "steamapps", "workshop", "content", "108600", "111", "mods", "AZ", "42");
            Directory.CreateDirectory(b42Dir);
            File.WriteAllText(Path.Combine(b42Dir, "mod.info"), "id=AuthenticZ\nname=Authentic Z\nversion=42\nincompatible=\\Lite\n");
            WriteIni(root, serverId, "WorkshopItems=111\nMods=AuthenticZ\n");

            ModDiscoveryResult result = await discovery.DiscoverAsync(serverId, CancellationToken.None);

            DiscoveredMod mod = result.InstalledItems.Single().Mods.Single();
            await Assert.That(mod.ModId).IsEqualTo("AuthenticZ");
            await Assert.That(mod.Version).IsEqualTo("42");
            string[] incompatible = ["\\Lite"];
            await Assert.That(mod.Incompatible).IsEquivalentTo(incompatible);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task An_item_directory_with_no_readable_mod_info_is_kept_with_no_mods()
    {
        (ModDiscovery discovery, string root, ServerId serverId) = NewDiscovery();
        try
        {
            // An item folder exists but its mods/ has no mod.info (a bad or partial download).
            string modsDir = Path.Combine(root, $"{serverId}.server", "steamapps", "workshop", "content", "108600", "111", "mods", "empty");
            Directory.CreateDirectory(modsDir);
            WriteIni(root, serverId, "WorkshopItems=111\nMods=\n");

            ModDiscoveryResult result = await discovery.DiscoverAsync(serverId, CancellationToken.None);

            DiscoveredWorkshopItem item = result.InstalledItems.Single();
            await Assert.That(item.WorkshopId).IsEqualTo("111");
            await Assert.That(item.Mods).IsEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
