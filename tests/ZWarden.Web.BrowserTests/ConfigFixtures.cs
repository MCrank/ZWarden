using ZWarden.Application.Configuration;

namespace ZWarden.Web.BrowserTests;

/// <summary>A small live configuration read (the same shape the real-host config tests use), so the schema-driven
/// editor renders without an Agent.</summary>
internal static class ConfigFixtures
{
    public static ConfigDocumentView SandboxView() => new(
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
        ],
        "VERSION = 1,\nZombies = 4,\n",
        "hash-abc",
        [],
        null);
}
