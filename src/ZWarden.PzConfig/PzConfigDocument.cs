using ZWarden.PzConfig.Internal;
using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig;

/// <summary>
/// The concrete document behind <see cref="IPzConfigDocument"/>. A document opened from bytes retains
/// an internal <see cref="IPzConfigEditBacking"/> (a Loretta syntax tree for Lua, the source text for
/// INI) so it can replace a value surgically and re-emit the file byte-for-byte (F20b). No parser type
/// crosses the public surface — the backing is internal — so the document is still safe to hand to any
/// tier (Loretta is MIT, parse-only, no native code). A document built directly from a value model
/// carries no backing and is read-only. Path reads walk the tree <em>iteratively</em>: the depth is
/// already bounded by the pre-check, and nothing on the config path recurses over attacker-controlled
/// depth (trust-boundaries §8).
/// </summary>
public sealed class PzConfigDocument : IPzConfigDocument
{
    private readonly IPzConfigEditBacking? _backing;
    private readonly PzTable? _root;

    /// <summary>Creates a read-only document of the given kind over the given root table.</summary>
    public PzConfigDocument(PzConfigKind kind, PzTable root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Kind = kind;
        _root = root;
    }

    /// <summary>Creates an editable document backed by a live parse, used by the readers.</summary>
    internal PzConfigDocument(PzConfigKind kind, IPzConfigEditBacking backing)
    {
        ArgumentNullException.ThrowIfNull(backing);
        Kind = kind;
        _backing = backing;
    }

    /// <inheritdoc/>
    public PzConfigKind Kind { get; }

    /// <inheritdoc/>
    public PzTable Root => _backing?.Model ?? _root!;

    /// <inheritdoc/>
    public bool IsEditable => _backing is not null;

    /// <inheritdoc/>
    public bool TryGetValue(string path, out PzValue value)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        PzValue current = Root;
        foreach (Range segment in SplitOnDots(path))
        {
            ReadOnlySpan<char> name = path.AsSpan()[segment];
            if (current is not PzTable table || !table.TryGet(name.ToString(), out PzValue next))
            {
                value = null!;
                return false;
            }

            current = next;
        }

        value = current;
        return true;
    }

    /// <inheritdoc/>
    public PzConfigEditResult TrySetValue(string path, PzValue newValue)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(newValue);
        return _backing is null ? PzConfigEditResult.NotEditable : _backing.TrySetValue(path, newValue);
    }

    /// <inheritdoc/>
    public byte[] Emit()
    {
        if (_backing is null)
        {
            throw new InvalidOperationException(
                "This document was not opened from bytes and cannot be emitted; regenerating a file from the model is forbidden (ADR 0010).");
        }

        return _backing.Emit();
    }

    // A dotted path is walked segment by segment with no recursion. Empty segments (a leading,
    // trailing or doubled dot) make the whole read fail rather than silently matching "".
    private static IEnumerable<Range> SplitOnDots(string path)
    {
        int start = 0;
        for (int i = 0; i <= path.Length; i++)
        {
            if (i == path.Length || path[i] == '.')
            {
                yield return start..i;
                start = i + 1;
            }
        }
    }
}
