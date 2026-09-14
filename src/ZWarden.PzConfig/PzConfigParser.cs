using ZWarden.PzConfig.Internal;

namespace ZWarden.PzConfig;

/// <summary>
/// The read side of the config seam: pre-check, then the reader for the kind. It is the one place
/// that guarantees the pre-check runs before a parser is invoked (ADR 0010; trust-boundaries §8), so
/// no caller can reach Loretta with unchecked bytes. Stateless and thread-safe.
/// </summary>
public sealed class PzConfigParser : IPzConfigParser
{
    /// <inheritdoc/>
    public PzConfigReadResult Open(PzConfigKind kind, ReadOnlySpan<byte> bytes) =>
        Open(kind, bytes, PzConfigLimits.Default);

    /// <inheritdoc/>
    public PzConfigReadResult Open(PzConfigKind kind, ReadOnlySpan<byte> bytes, PzConfigLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        PzConfigDiagnostic? preCheckFailure = PzConfigPreCheck.Check(bytes, kind, limits);
        if (preCheckFailure is not null)
        {
            return PzConfigReadResult.Failure(preCheckFailure);
        }

        return kind == PzConfigKind.Ini
            ? IniConfigReader.Read(bytes)
            : LuaConfigReader.Read(kind, bytes, limits.MaxNestingDepth);
    }
}
