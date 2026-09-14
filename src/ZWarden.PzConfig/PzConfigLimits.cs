namespace ZWarden.PzConfig;

/// <summary>
/// The ZWarden-side caps applied to raw bytes <em>before</em> any parser sees them (ADR 0010,
/// trust-boundaries §8). These exist because no Lua parser survives hostile input: the process dies
/// with an uncatchable <see cref="StackOverflowException"/> at deep nesting (research §5, ~1800
/// levels for Loretta), and .NET 10 has no AppDomain to sandbox it in. The pre-check is cheap and is
/// the only thing standing between an attacker-influenced file and the parser.
/// </summary>
public sealed record PzConfigLimits
{
    /// <summary>
    /// Maximum file size in bytes. PZ's real sandbox file is ~45 KB (research §2.1); the default is
    /// generous by two orders of magnitude while still bounding memory and parse time.
    /// </summary>
    public int MaxByteLength { get; init; } = 5 * 1024 * 1024;

    /// <summary>
    /// Maximum table-nesting depth. PZ's real files are depth 2 (sandbox) and 4 (spawnpoints)
    /// (research §5.6); 16 is generous by an order of magnitude and far below where any parser dies.
    /// </summary>
    public int MaxNestingDepth { get; init; } = 16;

    /// <summary>The default limits.</summary>
    public static PzConfigLimits Default { get; } = new();
}
