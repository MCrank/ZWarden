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
