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

    /// <summary>A short operator-facing name for the setting (e.g. <c>"Population"</c>), or null to fall back to the path.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// An override for the group a config editor files this setting under, or null (the norm) to use the
    /// <see cref="PzSettingCatalog"/> section.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>
    /// An authored tooltip that overrides the file's own comment. Usually null — F20c prefers the file
    /// comment (research §2.1) and reserves this for the few keys whose generated comment is unhelpful.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// True when ZWarden owns this key's value and no operator edit may change it (#228) — the INI ports, which the
    /// container template and the Agent's RCON connection depend on.
    /// </summary>
    public bool Managed { get; init; }

    /// <summary>Returns a copy with display metadata set; leaves type, range and default untouched.</summary>
    public PzSchemaEntry Display(string section, string label, string? description = null) =>
        this with { Section = section, Label = label, Description = description };

    /// <summary>
    /// Returns a copy with a friendly label, leaving the section to the setting catalog (#227) — the usual form, so
    /// the catalog's in-game grouping stays the one source of sections.
    /// </summary>
    public PzSchemaEntry Named(string label, string? description = null) =>
        this with { Label = label, Description = description };

    /// <summary>Returns a copy marked <see cref="Managed"/>.</summary>
    public PzSchemaEntry ManagedByZWarden() => this with { Managed = true };

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

    /// <summary>True when the key's value is owned by ZWarden and no edit may change it (#228).</summary>
    public static bool IsManaged(PzConfigKind kind, string path) =>
        For(kind) is { } schema && schema.TryGet(path, out PzSchemaEntry entry) && entry.Managed;

    /// <summary>Every rule in the schema.</summary>
    public IEnumerable<PzSchemaEntry> Entries => _entries.Values;

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

        PzSchemaEntry.Whole("Zombies", min: 1, max: 6, @default: 4).Named("Population"),
        PzSchemaEntry.Whole("Distribution", min: 1, max: 2, @default: 1).Named("Distribution"),
        PzSchemaEntry.Bool("ZombieVoronoiNoise").Named("Voronoi noise"),

        PzSchemaEntry.Table("Basement"),
        PzSchemaEntry.Whole("Basement.SpawnFrequency", min: 1, max: 7, @default: 4).Named("Spawn frequency"),

        PzSchemaEntry.Table("Map"),
        PzSchemaEntry.Bool("Map.AllowMiniMap", @default: false).Named("Allow mini-map"),
        PzSchemaEntry.Bool("Map.AllowWorldMap", @default: true).Named("Allow world map"),
        PzSchemaEntry.Whole("DayLength", min: 1, max: 27, @default: 4).Named("Day length"),
        PzSchemaEntry.Whole("WaterShutModifier", min: -1, max: int.MaxValue, @default: 14).Named("Water shutoff"),

        PzSchemaEntry.Table("ZombieLore"),
        PzSchemaEntry.Table("ZombieConfig"),

        PzSchemaEntry.Table("MultiplierConfig"),
        PzSchemaEntry.Num("MultiplierConfig.Glassmaking", min: 0, max: 1000).Named("Glassmaking XP"),
        PzSchemaEntry.Num("RollsMultiplier", min: 0.1, max: 100).Named("Loot rolls multiplier"),
    ]);

    /// <summary>A representative slice of the <c>&lt;name&gt;.ini</c> schema — the common server keys.</summary>
    public static PzSchema Ini { get; } = new(
    [
        PzSchemaEntry.Bool("PVP", @default: true).Named("PVP"),
        PzSchemaEntry.Bool("Open", @default: true).Named("Open server"),
        PzSchemaEntry.Bool("Public", @default: false).Named("List publicly"),
        PzSchemaEntry.Bool("PauseEmpty", @default: true).Named("Pause when empty"),
        PzSchemaEntry.Bool("GlobalChat", @default: true).Named("Global chat"),
        PzSchemaEntry.Text("PublicName").Named("Public name"),
        PzSchemaEntry.Text("ServerWelcomeMessage").Named("Welcome message"),
        PzSchemaEntry.Whole("MaxPlayers", min: 1, max: 254, @default: 32).Named("Max players"),
        PzSchemaEntry.Whole("DefaultPort", min: 0, max: 65535, @default: 16261).Named("Game port").ManagedByZWarden(),
        PzSchemaEntry.Whole("UDPPort", min: 0, max: 65535, @default: 16262).Named("UDP port").ManagedByZWarden(),
        PzSchemaEntry.Whole("RCONPort", min: 0, max: 65535, @default: 27015).Named("RCON port").ManagedByZWarden(),
        PzSchemaEntry.Text("RCONPassword").Named("RCON password"),
    ]);
}
