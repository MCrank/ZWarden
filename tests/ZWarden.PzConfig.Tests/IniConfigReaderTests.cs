using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Internal;
using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The small hand-written reader for <c>&lt;name&gt;.ini</c> — <c># comment</c> / <c>KEY=value</c>,
/// no sections, no continuation lines (research §7 scope note). Every value is a string; the schema
/// interprets bools and numbers. A malformed line is a diagnostic with a line number, not a throw,
/// and the other lines still read.
/// </summary>
public class IniConfigReaderTests
{
    private static PzConfigReadResult Read(string content) =>
        IniConfigReader.Read(Encoding.UTF8.GetBytes(content));

    private static PzConfigReadResult ReadBytes(byte[] content) =>
        IniConfigReader.Read(content);

    [Test]
    public async Task Reads_key_value_pairs_in_order_as_strings()
    {
        PzConfigReadResult result = Read("PVP=true\nMaxPlayers=32\nPublicName=My ZWarden Server\n");

        await Assert.That(result.Parsed).IsTrue();
        PzTable root = result.Document!.Root;
        string order = string.Join(",", root.NamedEntries.Select(e => e.Key!.Name));
        await Assert.That(order).IsEqualTo("PVP,MaxPlayers,PublicName");

        await Assert.That(result.Document.TryGetValue("MaxPlayers", out PzValue v)).IsTrue();
        await Assert.That(((PzString)v).Value).IsEqualTo("32");
    }

    [Test]
    public async Task Kind_is_ini()
    {
        PzConfigReadResult result = Read("PVP=true\n");
        await Assert.That(result.Document!.Kind).IsEqualTo(PzConfigKind.Ini);
    }

    [Test]
    public async Task Comment_and_blank_lines_are_ignored()
    {
        PzConfigReadResult result = Read("# this is a comment\n\nPVP=true\n   \n# another\nOpen=false\n");

        await Assert.That(result.Document!.Root.NamedEntries.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task Empty_value_becomes_an_empty_string()
    {
        PzConfigReadResult result = Read("RCONPassword=\n");

        await Assert.That(result.Document!.TryGetValue("RCONPassword", out PzValue v)).IsTrue();
        await Assert.That(((PzString)v).Value).IsEqualTo(string.Empty);
    }

    [Test]
    public async Task A_value_containing_equals_splits_on_the_first_one()
    {
        PzConfigReadResult result = Read("Mods=a=b=c\n");

        await Assert.That(result.Document!.TryGetValue("Mods", out PzValue v)).IsTrue();
        await Assert.That(((PzString)v).Value).IsEqualTo("a=b=c");
    }

    [Test]
    public async Task A_malformed_line_is_a_diagnostic_and_the_rest_still_reads()
    {
        PzConfigReadResult result = Read("PVP=true\nthis line has no equals\nOpen=false\n");

        await Assert.That(result.Parsed).IsTrue();
        await Assert.That(result.Document!.Root.NamedEntries.Count()).IsEqualTo(2);

        PzConfigDiagnostic diag = result.Diagnostics.Single();
        await Assert.That(diag.Severity).IsEqualTo(PzDiagnosticSeverity.Warning);
        await Assert.That(diag.Position!.Value.Line).IsEqualTo(2);
    }

    [Test]
    public async Task Crlf_line_endings_do_not_leak_into_the_value()
    {
        PzConfigReadResult result = Read("PublicName=My Server\r\nMaxPlayers=8\r\n");

        await Assert.That(result.Document!.TryGetValue("PublicName", out PzValue v)).IsTrue();
        await Assert.That(((PzString)v).Value).IsEqualTo("My Server");
    }

    [Test]
    public async Task A_leading_utf8_bom_is_stripped()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("PVP=true\n")];

        PzConfigReadResult result = ReadBytes(withBom);

        await Assert.That(result.Document!.TryGetValue("PVP", out _)).IsTrue();
    }
}
