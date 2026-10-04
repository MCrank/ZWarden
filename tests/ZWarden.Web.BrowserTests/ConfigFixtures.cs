using System.Globalization;
using System.Text;
using ZWarden.Application.Configuration;

namespace ZWarden.Web.BrowserTests;

/// <summary>A live configuration read (the same shape the real-host config tests use), so the schema-driven editor
/// renders without an Agent. It is as large as a real SandboxVars (#322): hundreds of settings and their raw text, so
/// the editor state is well past the Blazor hub's 32 KB receive limit and a test catches it being posted to the
/// circuit.</summary>
internal static class ConfigFixtures
{
    // Enough settings that the editor state is several times the 32 KB hub limit, like a real SandboxVars.lua.
    private const int FillerSettings = 400;

    public static ConfigDocumentView SandboxView()
    {
        List<ConfigSettingView> filler = [];
        StringBuilder raw = new("VERSION = 1,\nZombies = 4,\n");
        for (int i = 0; i < FillerSettings; i++)
        {
            string name = string.Create(CultureInfo.InvariantCulture, $"Setting{i:000}");
            filler.Add(new ConfigSettingView($"Filler.{name}", name, ConfigEditKind.Number, ConfigValueShape.Whole, "3",
                1, 5, "3", $"Sandbox option {name}: how often this part of the world behaves the way it does.",
                [new ConfigOption("1", "Never"), new ConfigOption("3", "Sometimes"), new ConfigOption("5", "Always")],
                KnownToSchema: true));
            raw.Append(CultureInfo.InvariantCulture, $"    {name} = 3,\n");
        }

        return new(
            ConfigReadOutcome.Read,
            [
                new ConfigSection("Access",
                [
                    new ConfigSettingView("PVP", "PVP", ConfigEditKind.Bool, ConfigValueShape.Boolean, "true",
                        null, null, null, "Allow player-versus-player combat.", [], KnownToSchema: true),
                    new ConfigSettingView("PublicName", "Public name", ConfigEditKind.Text, ConfigValueShape.Text,
                        "My Server", null, null, null, null, [], KnownToSchema: true),
                ]),
                new ConfigSection("Zombies",
                [
                    new ConfigSettingView("Zombies", "Population", ConfigEditKind.Number, ConfigValueShape.Whole, "4",
                        1, 6, "4", "The zombie population.",
                        [new ConfigOption("1", "Insane"), new ConfigOption("4", "Normal"), new ConfigOption("6", "None")],
                        KnownToSchema: true),
                ]),
                new ConfigSection("Filler", filler),
            ],
            raw.ToString(),
            "hash-abc",
            [],
            null);
    }
}
