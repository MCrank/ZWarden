using ZWarden.Agent.SteamCmd;

namespace ZWarden.Agent.Tests.SteamCmd;

/// <summary>#275: reading each installed Workshop item's <c>timeupdated</c> out of <c>appworkshop_108600.acf</c>.</summary>
public class SteamWorkshopManifestTests
{
    // Verbatim shape of the file PZ 42.21 writes (slice 0 spike, 2026-10-03).
    private const string SpikeManifest = """
        "AppWorkshop"
        {
        	"appid"		"108600"
        	"SizeOnDisk"		"10059785"
        	"NeedsUpdate"		"0"
        	"NeedsDownload"		"0"
        	"TimeLastUpdated"		"0"
        	"TimeLastFullCheck"		"0"
        	"TimeLastAppRan"		"0"
        	"LastBuildID"		"0"
        	"WorkshopItemsInstalled"
        	{
        		"2392709985"
        		{
        			"size"		"9609530"
        			"timeupdated"		"1687374018"
        			"manifest"		"506767093651292732"
        		}
        		"2553809727"
        		{
        			"size"		"450255"
        			"timeupdated"		"1789036314"
        			"manifest"		"8134259270852217296"
        		}
        	}
        	"WorkshopItemDetails"
        	{
        		"2392709985"
        		{
        			"manifest"		"506767093651292732"
        			"timeupdated"		"1600000000"
        			"timetouched"		"1791063999"
        			"latest_timeupdated"		"1687374018"
        			"latest_manifest"		"506767093651292732"
        		}
        	}
        }
        """;

    [Test]
    public async Task It_reads_each_installed_items_timeupdated()
    {
        IReadOnlyDictionary<string, DateTimeOffset> times = SteamWorkshopManifest.ParseInstalledTimeUpdated(SpikeManifest);

        await Assert.That(times.Count).IsEqualTo(2);
        await Assert.That(times["2392709985"]).IsEqualTo(DateTimeOffset.FromUnixTimeSeconds(1687374018));
        await Assert.That(times["2553809727"]).IsEqualTo(DateTimeOffset.FromUnixTimeSeconds(1789036314));
    }

    [Test]
    public async Task Only_the_installed_block_counts_not_the_details_block()
    {
        // WorkshopItemDetails carries its own timeupdated (here deliberately different); the installed copy is the
        // WorkshopItemsInstalled one.
        IReadOnlyDictionary<string, DateTimeOffset> times = SteamWorkshopManifest.ParseInstalledTimeUpdated(SpikeManifest);

        await Assert.That(times["2392709985"].ToUnixTimeSeconds()).IsEqualTo(1687374018);
    }

    [Test]
    public async Task A_manifest_without_an_installed_block_yields_nothing()
    {
        string manifest = """
            "AppWorkshop"
            {
            	"appid"		"108600"
            	"WorkshopItemDetails"
            	{
            		"2553809727" { "timeupdated" "1789036314" }
            	}
            }
            """;

        await Assert.That(SteamWorkshopManifest.ParseInstalledTimeUpdated(manifest).Count).IsEqualTo(0);
    }

    [Test]
    public async Task Unparseable_text_yields_nothing()
    {
        await Assert.That(SteamWorkshopManifest.ParseInstalledTimeUpdated("not a manifest { at all").Count).IsEqualTo(0);
    }

    [Test]
    public async Task An_unbalanced_installed_block_yields_nothing()
    {
        string manifest = "\"AppWorkshop\" { \"WorkshopItemsInstalled\" { \"2553809727\" { \"timeupdated\" \"1789036314\" ";

        await Assert.That(SteamWorkshopManifest.ParseInstalledTimeUpdated(manifest).Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("0")]
    [Arguments("-5")]
    [Arguments("abc")]
    [Arguments("99999999999999999999")]
    public async Task A_zero_or_bogus_timeupdated_is_skipped(string value)
    {
        string manifest = $$"""
            "AppWorkshop"
            {
            	"WorkshopItemsInstalled"
            	{
            		"2553809727" { "timeupdated" "{{value}}" }
            		"2392709985" { "timeupdated" "1687374018" }
            	}
            }
            """;

        IReadOnlyDictionary<string, DateTimeOffset> times = SteamWorkshopManifest.ParseInstalledTimeUpdated(manifest);

        await Assert.That(times.ContainsKey("2553809727")).IsFalse();
        await Assert.That(times.ContainsKey("2392709985")).IsTrue();
    }

    [Test]
    public async Task A_non_numeric_item_key_is_skipped()
    {
        string manifest = """
            "AppWorkshop"
            {
            	"WorkshopItemsInstalled"
            	{
            		"../etc" { "timeupdated" "1687374018" }
            		"2392709985" { "timeupdated" "1687374018" }
            	}
            }
            """;

        IReadOnlyDictionary<string, DateTimeOffset> times = SteamWorkshopManifest.ParseInstalledTimeUpdated(manifest);

        await Assert.That(times.Keys.ToArray()).IsEquivalentTo(["2392709985"]);
    }

    [Test]
    public async Task An_item_without_a_timeupdated_is_skipped()
    {
        string manifest = """
            "AppWorkshop" { "WorkshopItemsInstalled" { "2553809727" { "size" "450255" } } }
            """;

        await Assert.That(SteamWorkshopManifest.ParseInstalledTimeUpdated(manifest).Count).IsEqualTo(0);
    }
}
