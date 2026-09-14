using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Internal;
using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The write half for <c>&lt;name&gt;.ini</c> (F20b, ADR 0010): a surgical replacement of the value
/// after <c>=</c> on the matching line, preserving every other byte — key, spacing, trailing comment,
/// and the exact line endings — and emitting BOM-less UTF-8. A missing key is a first-class result, not
/// a throw; a document built without a parse backing is not editable.
/// </summary>
public class IniConfigEditTests
{
    private static IPzConfigDocument Open(string content) =>
        IniConfigReader.Read(Encoding.UTF8.GetBytes(content)).Document!;

    [Test]
    public async Task Emit_of_an_unedited_document_is_byte_identical()
    {
        const string src = "# a comment\nPVP=true\nMaxPlayers=32\nPublicName=My Server\n";

        IPzConfigDocument doc = Open(src);
        byte[] emitted = doc.Emit();

        await Assert.That(emitted).IsEquivalentTo(Encoding.UTF8.GetBytes(src));
    }

    [Test]
    public async Task Setting_a_value_changes_only_that_line()
    {
        // The INI has whole-line '#' comments but no inline comments: the value is everything after
        // '=' (matching real servertest.ini), so a set replaces exactly that remainder.
        const string src = "# header comment is preserved\nPVP=true\nMaxPlayers=32\nOpen=false\n";

        IPzConfigDocument doc = Open(src);
        PzConfigEditResult result = doc.TrySetValue("MaxPlayers", new PzString("48"));

        await Assert.That(result.Ok).IsTrue();

        const string expected = "# header comment is preserved\nPVP=true\nMaxPlayers=48\nOpen=false\n";
        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo(expected);
    }

    [Test]
    public async Task An_edited_value_reads_back_through_the_model()
    {
        IPzConfigDocument doc = Open("MaxPlayers=32\n");

        doc.TrySetValue("MaxPlayers", new PzString("48"));

        await Assert.That(doc.TryGetValue("MaxPlayers", out PzValue v)).IsTrue();
        await Assert.That(((PzString)v).Value).IsEqualTo("48");
    }

    [Test]
    public async Task Crlf_endings_are_preserved_on_emit()
    {
        const string src = "PublicName=My Server\r\nMaxPlayers=8\r\n";

        IPzConfigDocument doc = Open(src);
        doc.TrySetValue("MaxPlayers", new PzString("16"));

        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo("PublicName=My Server\r\nMaxPlayers=16\r\n");
    }

    [Test]
    public async Task A_number_or_boolean_value_is_rendered_as_raw_ini_text()
    {
        IPzConfigDocument doc = Open("MaxPlayers=32\nPVP=true\n");

        await Assert.That(doc.TrySetValue("MaxPlayers", new PzNumber(16, "16", isInteger: true)).Ok).IsTrue();
        await Assert.That(doc.TrySetValue("PVP", new PzBoolean(false)).Ok).IsTrue();

        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo("MaxPlayers=16\nPVP=false\n");
    }

    [Test]
    public async Task Setting_a_missing_key_is_a_first_class_result_and_changes_nothing()
    {
        const string src = "MaxPlayers=32\n";
        IPzConfigDocument doc = Open(src);

        PzConfigEditResult result = doc.TrySetValue("NoSuchKey", new PzString("1"));

        await Assert.That(result.Ok).IsFalse();
        await Assert.That(result.Status).IsEqualTo(PzEditStatus.PathNotFound);
        await Assert.That(Encoding.UTF8.GetString(doc.Emit())).IsEqualTo(src);
    }

    [Test]
    public async Task A_dotted_path_never_matches_in_a_flat_ini()
    {
        IPzConfigDocument doc = Open("MaxPlayers=32\n");

        PzConfigEditResult result = doc.TrySetValue("Map.MaxPlayers", new PzString("1"));

        await Assert.That(result.Status).IsEqualTo(PzEditStatus.PathNotFound);
    }

    [Test]
    public async Task Emit_never_writes_a_bom_even_when_the_input_had_one()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("PVP=true\n")];

        IPzConfigDocument doc = IniConfigReader.Read(withBom).Document!;
        byte[] emitted = doc.Emit();

        await Assert.That(emitted.Length >= 3 && emitted[0] == 0xEF && emitted[1] == 0xBB && emitted[2] == 0xBF).IsFalse();
        await Assert.That(Encoding.UTF8.GetString(emitted)).IsEqualTo("PVP=true\n");
    }

    [Test]
    public async Task A_document_built_without_a_backing_is_not_editable()
    {
        var doc = new PzConfigDocument(PzConfigKind.Ini, new PzTable([new PzTableEntry(PzKey.Identifier("PVP"), new PzString("true"))]));

        await Assert.That(doc.IsEditable).IsFalse();
        await Assert.That(doc.TrySetValue("PVP", new PzString("false")).Status).IsEqualTo(PzEditStatus.NotEditable);
    }
}
