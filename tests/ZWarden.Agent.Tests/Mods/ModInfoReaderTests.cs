using System.Text;
using ZWarden.Agent.Mods;

namespace ZWarden.Agent.Tests.Mods;

/// <summary>
/// F21: the hand-written <c>mod.info</c> reader. A <c>mod.info</c> is a flat UTF-8 <c>key=value</c> text file
/// (never Lua, not one of PZ's four config files); the reader pulls the <c>id=</c> (the token the config's
/// <c>Mods=</c> line enables) and the optional <c>name=</c>. It gets untrusted-input hygiene (trust-boundaries §8):
/// a byte-size cap before parsing, bounded field lengths, a BOM tolerated, a malformed line skipped rather than
/// throwing, and "no usable id" reported as <c>null</c> (not a mappable mod).
/// </summary>
public class ModInfoReaderTests
{
    private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);

    [Test]
    public async Task Reads_the_id_and_name()
    {
        ModInfo? info = ModInfoReader.Read(Utf8("name=Brita's Weapon Pack\nid=Brita_2\nposter=poster.png\n"));

        await Assert.That(info).IsNotNull();
        await Assert.That(info!.Id).IsEqualTo("Brita_2");
        await Assert.That(info.Name).IsEqualTo("Brita's Weapon Pack");
    }

    [Test]
    public async Task Trims_whitespace_around_keys_and_values()
    {
        ModInfo? info = ModInfoReader.Read(Utf8("  id =  ModA \r\n name = Mod A \r\n"));

        await Assert.That(info!.Id).IsEqualTo("ModA");
        await Assert.That(info.Name).IsEqualTo("Mod A");
    }

    [Test]
    public async Task A_missing_name_is_null_but_the_id_still_reads()
    {
        ModInfo? info = ModInfoReader.Read(Utf8("id=NoName\n"));

        await Assert.That(info!.Id).IsEqualTo("NoName");
        await Assert.That(info.Name).IsNull();
    }

    [Test]
    public async Task No_id_yields_null_because_the_mod_cannot_be_mapped()
    {
        await Assert.That(ModInfoReader.Read(Utf8("name=Orphan\ndescription=no id here\n"))).IsNull();
        await Assert.That(ModInfoReader.Read(Utf8("id=\nname=Blank\n"))).IsNull();
    }

    [Test]
    public async Task Malformed_lines_are_skipped_not_thrown()
    {
        ModInfo? info = ModInfoReader.Read(Utf8("this line has no equals\nid=Survivor\n\n=leadingEquals\n"));

        await Assert.That(info!.Id).IsEqualTo("Survivor");
    }

    [Test]
    public async Task An_id_with_spaces_is_preserved()
    {
        // Real Mod ids contain spaces, e.g. "Authentic Z - Current" (research §6) — internal spaces must survive.
        ModInfo? info = ModInfoReader.Read(Utf8("id=Authentic Z - Current\nname=Authentic Z\n"));

        await Assert.That(info!.Id).IsEqualTo("Authentic Z - Current");
    }

    [Test]
    public async Task The_first_id_wins_when_the_file_repeats_it()
    {
        ModInfo? info = ModInfoReader.Read(Utf8("id=First\nid=Second\n"));

        await Assert.That(info!.Id).IsEqualTo("First");
    }

    [Test]
    public async Task A_leading_utf8_bom_is_tolerated()
    {
        byte[] bom = [0xEF, 0xBB, 0xBF, .. Utf8("id=WithBom\nname=Bommed\n")];

        ModInfo? info = ModInfoReader.Read(bom);

        await Assert.That(info!.Id).IsEqualTo("WithBom");
        await Assert.That(info.Name).IsEqualTo("Bommed");
    }

    [Test]
    public async Task Reads_version_pz_version_and_version_min()
    {
        // #110: research §6 keys — version=, pzversion=, versionMin= (scalars, first value wins like id/name).
        ModInfo? info = ModInfoReader.Read(Utf8("id=AZ\nversion=42\npzversion=41\nversionMin=41.78\n"));

        await Assert.That(info!.Version).IsEqualTo("42");
        await Assert.That(info.PzVersion).IsEqualTo("41");
        await Assert.That(info.VersionMin).IsEqualTo("41.78");
    }

    [Test]
    public async Task Reads_require_as_a_comma_separated_list()
    {
        ModInfo? info = ModInfoReader.Read(Utf8("id=AZ\nrequire=RV_Interior_MP, OtherDep\n"));

        string[] expected = ["RV_Interior_MP", "OtherDep"];
        await Assert.That(info!.Requires).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Accumulates_require_across_repeated_lines()
    {
        // require= may appear on multiple lines and/or be comma-separated; all are collected in order.
        ModInfo? info = ModInfoReader.Read(Utf8("id=AZ\nrequire=DepA\nrequire=DepB,DepC\n"));

        string[] expected = ["DepA", "DepB", "DepC"];
        await Assert.That(info!.Requires).IsEquivalentTo(expected);
    }

    [Test]
    public async Task Reads_incompatible_and_tags_verbatim()
    {
        // incompatible values carry PZ's leading '\' and trailing '+'/'-' markers (research §6) — kept verbatim.
        ModInfo? info = ModInfoReader.Read(Utf8("id=AZ\nincompatible=\\AuthenticZLite,\\AuthenticZBackpacks+\ntags=Realistic,Overhaul\n"));

        string[] incompatible = ["\\AuthenticZLite", "\\AuthenticZBackpacks+"];
        string[] tags = ["Realistic", "Overhaul"];
        await Assert.That(info!.Incompatible).IsEquivalentTo(incompatible);
        await Assert.That(info.Tags).IsEquivalentTo(tags);
    }

    [Test]
    public async Task Absent_optional_fields_default_empty_or_null()
    {
        ModInfo? info = ModInfoReader.Read(Utf8("id=Plain\n"));

        await Assert.That(info!.Version).IsNull();
        await Assert.That(info.PzVersion).IsNull();
        await Assert.That(info.VersionMin).IsNull();
        await Assert.That(info.Requires).IsEmpty();
        await Assert.That(info.Incompatible).IsEmpty();
        await Assert.That(info.Tags).IsEmpty();
    }

    [Test]
    public async Task List_field_item_count_is_bounded()
    {
        string many = string.Join(',', Enumerable.Range(0, ModInfoReader.MaxListItems + 50).Select(i => "d" + i));
        ModInfo? info = ModInfoReader.Read(Utf8("id=AZ\nrequire=" + many + "\n"));

        await Assert.That(info!.Requires.Count).IsEqualTo(ModInfoReader.MaxListItems);
    }

    [Test]
    public async Task List_field_elements_are_length_bounded()
    {
        ModInfo? info = ModInfoReader.Read(Utf8("id=AZ\nrequire=" + new string('a', 5000) + "\n"));

        await Assert.That(info!.Requires.Single().Length).IsEqualTo(ModInfoReader.MaxFieldLength);
    }

    [Test]
    public async Task Oversized_content_is_rejected_before_parsing()
    {
        byte[] huge = Utf8("id=Big\n" + new string('x', ModInfoReader.MaxBytes + 1));

        await Assert.That(ModInfoReader.Read(huge)).IsNull();
    }

    [Test]
    public async Task Over_long_fields_are_bounded()
    {
        ModInfo? info = ModInfoReader.Read(Utf8("id=" + new string('a', 5000) + "\n"));

        await Assert.That(info!.Id.Length).IsEqualTo(ModInfoReader.MaxFieldLength);
    }
}
