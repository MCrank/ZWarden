using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig;

/// <summary>
/// The concrete parsed document behind <see cref="IPzConfigDocument"/>. It holds only ZWarden's own
/// value model — no parser type is retained — so it is safe to hand to any tier. Path reads walk the
/// tree <em>iteratively</em>: the depth is already bounded by the pre-check, and nothing on the
/// config path recurses over attacker-controlled depth (trust-boundaries §8).
/// </summary>
public sealed class PzConfigDocument : IPzConfigDocument
{
    /// <summary>Creates a document of the given kind over the given root table.</summary>
    public PzConfigDocument(PzConfigKind kind, PzTable root)
    {
        ArgumentNullException.ThrowIfNull(root);
        Kind = kind;
        Root = root;
    }

    /// <inheritdoc/>
    public PzConfigKind Kind { get; }

    /// <inheritdoc/>
    public PzTable Root { get; }

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
