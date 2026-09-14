using ZWarden.PzConfig;
using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// The value model: a table preserves order, keeps positional and named entries distinct, keeps
/// keys ZWarden has no schema for, and reads by dotted path iteratively. These are the guarantees
/// the readers and the validator are written against.
/// </summary>
public class PzValueModelTests
{
    private static PzTable Table(params PzTableEntry[] entries) => new(entries);

    private static PzTableEntry Named(string name, PzValue value) => new(PzKey.Identifier(name), value);

    [Test]
    public async Task Table_preserves_entry_order()
    {
        PzTable table = Table(
            Named("Zombies", new PzNumber(4, "4", isInteger: true)),
            Named("Distribution", new PzNumber(1, "1", isInteger: true)),
            Named("VERSION", new PzNumber(6, "6", isInteger: true)));

        string order = string.Join(",", table.NamedEntries.Select(e => e.Key!.Name));

        await Assert.That(order).IsEqualTo("Zombies,Distribution,VERSION");
    }

    [Test]
    public async Task Table_separates_positional_from_named_entries()
    {
        PzTable table = Table(
            new PzTableEntry(Key: null, new PzString("first")),
            Named("name", new PzString("Muldraugh, KY")),
            new PzTableEntry(Key: null, new PzString("second")));

        await Assert.That(table.PositionalEntries.Count()).IsEqualTo(2);
        await Assert.That(table.NamedEntries.Count()).IsEqualTo(1);
    }

    [Test]
    public async Task TryGet_finds_a_named_value_and_misses_an_absent_one()
    {
        PzTable table = Table(Named("AllowMiniMap", new PzBoolean(false)));

        await Assert.That(table.TryGet("AllowMiniMap", out PzValue value)).IsTrue();
        await Assert.That(((PzBoolean)value).Value).IsFalse();
        await Assert.That(table.TryGet("Nope", out _)).IsFalse();
    }

    [Test]
    public async Task Quoted_key_records_the_logical_name_and_that_it_was_quoted()
    {
        PzKey key = PzKey.Quoted("park ranger");

        await Assert.That(key.Name).IsEqualTo("park ranger");
        await Assert.That(key.WasQuoted).IsTrue();
    }

    [Test]
    public async Task Document_reads_a_nested_value_by_dotted_path()
    {
        PzTable map = Table(Named("AllowMiniMap", new PzBoolean(false)));
        PzTable root = Table(Named("Map", map));
        var document = new PzConfigDocument(PzConfigKind.SandboxVars, root);

        await Assert.That(document.TryGetValue("Map.AllowMiniMap", out PzValue value)).IsTrue();
        await Assert.That(((PzBoolean)value).Value).IsFalse();
    }

    [Test]
    public async Task Document_path_read_fails_when_an_intermediate_segment_is_not_a_table()
    {
        PzTable root = Table(Named("Zombies", new PzNumber(4, "4", isInteger: true)));
        var document = new PzConfigDocument(PzConfigKind.SandboxVars, root);

        // "Zombies" is a number, so descending into "Zombies.Anything" cannot resolve.
        await Assert.That(document.TryGetValue("Zombies.Anything", out _)).IsFalse();
    }

    [Test]
    public async Task Document_path_read_fails_on_a_trailing_dot()
    {
        PzTable root = Table(Named("Map", PzTable.Empty));
        var document = new PzConfigDocument(PzConfigKind.SandboxVars, root);

        await Assert.That(document.TryGetValue("Map.", out _)).IsFalse();
    }
}
