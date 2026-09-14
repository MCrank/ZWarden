using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Validation;

/// <summary>The type a schema expects for a key's value.</summary>
public enum PzValueType
{
    /// <summary>A boolean (or, in the INI, a value that reads as one).</summary>
    Boolean,

    /// <summary>A whole number (integer-valued).</summary>
    Whole,

    /// <summary>Any number (integer or fractional).</summary>
    Number,

    /// <summary>A text value.</summary>
    Text,

    /// <summary>A nested table; validation descends into it.</summary>
    Table,
}

/// <summary>
/// One key's validation rule. The set of these is ZWarden's own, maintained by hand — the ranges and
/// defaults are Java-side in Project Zomboid and cannot be derived from the shipped Lua (ADR 0010), so
/// this is deliberate, ongoing manual work. A key with no entry is not an error: it is preserved and
/// reported as informational, so a mod-added or new-patch key is never lost.
/// </summary>
public sealed record PzSchemaEntry
{
    /// <summary>The dotted path from the root, e.g. <c>"Map.AllowMiniMap"</c>.</summary>
    public required string Path { get; init; }

    /// <summary>The expected value type.</summary>
    public required PzValueType Type { get; init; }

    /// <summary>Inclusive minimum for a numeric value, if bounded.</summary>
    public double? Min { get; init; }

    /// <summary>Inclusive maximum for a numeric value, if bounded.</summary>
    public double? Max { get; init; }

    /// <summary>The game default, if recorded — a reference for F20b's reset/apply, not used to validate.</summary>
    public PzValue? Default { get; init; }

    /// <summary>A boolean rule.</summary>
    public static PzSchemaEntry Bool(string path, bool? @default = null) =>
        new() { Path = path, Type = PzValueType.Boolean, Default = @default is bool b ? new PzBoolean(b) : null };

    /// <summary>A whole-number rule with an optional inclusive range and default.</summary>
    public static PzSchemaEntry Whole(string path, double? min = null, double? max = null, int? @default = null) =>
        new()
        {
            Path = path,
            Type = PzValueType.Whole,
            Min = min,
            Max = max,
            Default = @default is int d ? new PzNumber(d, d.ToString(System.Globalization.CultureInfo.InvariantCulture), isInteger: true) : null,
        };

    /// <summary>A number rule with an optional inclusive range.</summary>
    public static PzSchemaEntry Num(string path, double? min = null, double? max = null) =>
        new() { Path = path, Type = PzValueType.Number, Min = min, Max = max };

    /// <summary>A text rule.</summary>
    public static PzSchemaEntry Text(string path) => new() { Path = path, Type = PzValueType.Text };

    /// <summary>A nested-table rule; validation descends into a matching table.</summary>
    public static PzSchemaEntry Table(string path) => new() { Path = path, Type = PzValueType.Table };
}

/// <summary>
/// A validation schema: the rules for one config kind, keyed by dotted path. The built-in schemas here
/// are a representative subset (ADR 0010; F20a mini-plan) — the mechanism and the structural rules plus
/// a well-tested slice of high-value keys. Filling out the remaining keys is incremental work: one
/// entry and one test each, no code change.
/// </summary>
public sealed class PzSchema
{
    private readonly Dictionary<string, PzSchemaEntry> _entries;

    /// <summary>Builds a schema from its entries.</summary>
    public PzSchema(IEnumerable<PzSchemaEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        _entries = entries.ToDictionary(e => e.Path, StringComparer.Ordinal);
    }

    /// <summary>Looks up the rule for a dotted path.</summary>
    public bool TryGet(string path, out PzSchemaEntry entry) => _entries.TryGetValue(path, out entry!);

    /// <summary>The schema for a kind, or <see langword="null"/> for the spawn files (validated structurally).</summary>
    public static PzSchema? For(PzConfigKind kind) => kind switch
    {
        PzConfigKind.SandboxVars => SandboxVars,
        PzConfigKind.Ini => Ini,
        _ => null,
    };

    /// <summary>
    /// A representative slice of the sandbox schema: top-level population/lore keys, all five nested
    /// tables (research §2.1), and sample keys within them. Ranges are ZWarden's own (research §2.1).
    /// </summary>
    public static PzSchema SandboxVars { get; } = new(
    [
        PzSchemaEntry.Whole("VERSION", min: 1),
        PzSchemaEntry.Whole("Zombies", min: 1, max: 6, @default: 4),
        PzSchemaEntry.Whole("Distribution", min: 1, max: 2, @default: 1),
        PzSchemaEntry.Bool("ZombieVoronoiNoise"),

        PzSchemaEntry.Table("Basement"),
        PzSchemaEntry.Whole("Basement.SpawnFrequency", min: 1, max: 7, @default: 4),

        PzSchemaEntry.Table("Map"),
        PzSchemaEntry.Bool("Map.AllowMiniMap", @default: false),
        PzSchemaEntry.Bool("Map.AllowWorldMap", @default: true),

        PzSchemaEntry.Table("ZombieLore"),
        PzSchemaEntry.Table("ZombieConfig"),
        PzSchemaEntry.Table("MultiplierConfig"),
    ]);

    /// <summary>A representative slice of the <c>&lt;name&gt;.ini</c> schema — the common server keys.</summary>
    public static PzSchema Ini { get; } = new(
    [
        PzSchemaEntry.Bool("PVP", @default: true),
        PzSchemaEntry.Bool("Open", @default: true),
        PzSchemaEntry.Bool("Public", @default: false),
        PzSchemaEntry.Bool("PauseEmpty", @default: true),
        PzSchemaEntry.Bool("GlobalChat", @default: true),
        PzSchemaEntry.Text("PublicName"),
        PzSchemaEntry.Text("ServerWelcomeMessage"),
        PzSchemaEntry.Whole("MaxPlayers", min: 1, max: 100, @default: 32),
        PzSchemaEntry.Whole("DefaultPort", min: 0, max: 65535, @default: 16261),
        PzSchemaEntry.Whole("UDPPort", min: 0, max: 65535, @default: 16262),
        PzSchemaEntry.Whole("RCONPort", min: 0, max: 65535, @default: 27015),
        PzSchemaEntry.Text("RCONPassword"),
    ]);
}
