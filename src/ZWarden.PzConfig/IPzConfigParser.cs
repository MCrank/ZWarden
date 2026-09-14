namespace ZWarden.PzConfig;

/// <summary>
/// Opens a Project Zomboid config file: turns raw bytes plus a <see cref="PzConfigKind"/> into a
/// parsed <see cref="IPzConfigDocument"/>, or a first-class not-parsed result. This is the read entry
/// point of the F20a seam (ADR 0010); it always applies ZWarden's size and nesting-depth pre-check
/// <em>before</em> any parser sees the bytes. Callers supply the bytes — from the Agent's <c>/pz/</c>
/// bind mount, or a test fixture; where the bytes come from is not this component's concern.
/// </summary>
public interface IPzConfigParser
{
    /// <summary>Opens a file with the default limits (<see cref="PzConfigLimits.Default"/>).</summary>
    PzConfigReadResult Open(PzConfigKind kind, ReadOnlySpan<byte> bytes);

    /// <summary>Opens a file with explicit limits.</summary>
    PzConfigReadResult Open(PzConfigKind kind, ReadOnlySpan<byte> bytes, PzConfigLimits limits);
}
