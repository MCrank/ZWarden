namespace ZWarden.PzConfig.Model;

/// <summary>
/// A table key. PZ writes bare identifiers (<c>Zombies = …</c>) and, where a name needs quoting,
/// a bracketed string key (<c>["park ranger"] = …</c>) (research §2.2). <see cref="Name"/> is the
/// logical name in both cases; <see cref="WasQuoted"/> preserves which form the file used, for
/// F20b's fidelity-preserving emit.
/// </summary>
public sealed record PzKey(string Name, bool WasQuoted)
{
    /// <summary>A bare-identifier key.</summary>
    public static PzKey Identifier(string name) => new(name, WasQuoted: false);

    /// <summary>A bracketed, quoted key such as <c>["park ranger"]</c>.</summary>
    public static PzKey Quoted(string name) => new(name, WasQuoted: true);
}

/// <summary>
/// One entry in a <see cref="PzTable"/>. A named entry (<see cref="Key"/> non-null) is a
/// <c>Key = value</c> field; a positional entry (<see cref="Key"/> null) is a bare element of a Lua
/// sequence, such as one spawn region or one spawn cell.
/// </summary>
public sealed record PzTableEntry(PzKey? Key, PzValue Value)
{
    /// <summary><see langword="true"/> when this is a bare sequence element rather than a keyed field.</summary>
    public bool IsPositional => Key is null;
}

/// <summary>
/// A Lua table (or, for the INI, the flat set of key=value lines). Entries keep the file's order,
/// mix named and positional forms, and include keys with no schema entry — nothing is dropped or
/// reordered. Lookups are read-only; F20a never mutates the model.
/// </summary>
public sealed class PzTable : PzValue
{
    private readonly List<PzTableEntry> _entries;

    /// <summary>Initializes a table from an ordered sequence of entries.</summary>
    public PzTable(IEnumerable<PzTableEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = [.. entries];
    }

    /// <summary>An empty table.</summary>
    public static PzTable Empty { get; } = new([]);

    /// <summary>Every entry, in file order.</summary>
    public IReadOnlyList<PzTableEntry> Entries => _entries;

    /// <summary>The named (<c>Key = value</c>) entries, in file order.</summary>
    public IEnumerable<PzTableEntry> NamedEntries => _entries.Where(e => e.Key is not null);

    /// <summary>The positional (bare sequence) entries, in file order.</summary>
    public IEnumerable<PzTableEntry> PositionalEntries => _entries.Where(e => e.IsPositional);

    /// <summary>
    /// Finds the value of the first named entry whose key matches <paramref name="name"/> (ordinal).
    /// PZ's files do not repeat a key, so "first" and "the" coincide.
    /// </summary>
    public bool TryGet(string name, out PzValue value)
    {
        ArgumentNullException.ThrowIfNull(name);
        foreach (PzTableEntry entry in _entries)
        {
            if (entry.Key is { } key && string.Equals(key.Name, name, StringComparison.Ordinal))
            {
                value = entry.Value;
                return true;
            }
        }

        value = null!;
        return false;
    }
}
