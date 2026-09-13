using ZWarden.Agent.SteamCmd;

namespace ZWarden.Agent.Tests.SteamCmd;

/// <summary>F17: reading the installed build id out of Steam's app manifest (KeyValues text).</summary>
public class SteamAppManifestTests
{
    [Test]
    public async Task It_reads_the_buildid_from_a_manifest()
    {
        string manifest = """
            "AppState"
            {
                "appid"		"380870"
                "buildid"		"24909836"
                "StateFlags"		"4"
            }
            """;

        await Assert.That(SteamAppManifest.ParseBuildId(manifest)).IsEqualTo("24909836");
    }

    [Test]
    public async Task A_manifest_without_a_buildid_yields_null()
    {
        await Assert.That(SteamAppManifest.ParseBuildId("\"AppState\" { \"appid\" \"380870\" }")).IsNull();
    }

    [Test]
    public async Task Unparseable_text_yields_null()
    {
        await Assert.That(SteamAppManifest.ParseBuildId("not a manifest at all")).IsNull();
    }
}
