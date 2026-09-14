using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Revisions;

/// <summary>One scalar leaf of a document, at its dotted path. The unit a snapshot flattens to.</summary>
/// <param name="Path">The dotted path (with <c>[i]</c> for positional entries).</param>
/// <param name="Value">The scalar value (never a table).</param>
public sealed record PzScalarEntry(string Path, PzValue Value);

/// <summary>
/// A canonical, order-normalized view of a configuration document's values (F20b, ADR 0011). The
/// document is flattened to its scalar leaves, sorted by path, so a key reorder — which the server
/// performs on every start — produces an identical snapshot and hash, while a genuine value change does
/// not. This is what a Configuration Revision persists (the parsed values, not bytes) and what the
/// drift check fingerprints. The flatten is iterative; the model it walks is already depth-bounded.
/// </summary>
public sealed class PzValueSnapshot
{
    private PzValueSnapshot(IReadOnlyList<PzScalarEntry> scalars, string canonicalText, string hash)
    {
        Scalars = scalars;
        CanonicalText = canonicalText;
        Hash = hash;
    }

    /// <summary>The scalar leaves, sorted by path — enough to reconstruct the values on restore.</summary>
    public IReadOnlyList<PzScalarEntry> Scalars { get; }

    /// <summary>The deterministic serialization the <see cref="Hash"/> is taken over.</summary>
    public string CanonicalText { get; }

    /// <summary>Lowercase-hex SHA-256 of the canonical form; the drift baseline fingerprint.</summary>
    public string Hash { get; }

    /// <summary>Snapshots a document's values.</summary>
    public static PzValueSnapshot Of(IPzConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Of(document.Root);
    }

    /// <summary>Snapshots a value tree.</summary>
    public static PzValueSnapshot Of(PzTable root)
    {
        ArgumentNullException.ThrowIfNull(root);

        List<PzScalarEntry> scalars = Flatten(root);
        scalars.Sort(static (x, y) => string.CompareOrdinal(x.Path, y.Path));

        // A JSON array of [path, encoded-value] pairs: STJ escapes both, so a value containing a
        // separator-like character cannot collide with the structure, and the order is fixed by the sort.
        string canonicalText = JsonSerializer.Serialize(scalars.Select(s => new[] { s.Path, Encode(s.Value) }));
        string hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText)));
        return new PzValueSnapshot(scalars, canonicalText, hash);
    }

    private static List<PzScalarEntry> Flatten(PzTable root)
    {
        var scalars = new List<PzScalarEntry>();
        var stack = new Stack<Frame>();
        stack.Push(new Frame(string.Empty, root));

        while (stack.Count > 0)
        {
            Frame frame = stack.Pop();

            foreach (PzTableEntry entry in frame.Table.NamedEntries)
            {
                string path = frame.Prefix.Length == 0 ? entry.Key!.Name : $"{frame.Prefix}.{entry.Key!.Name}";
                Visit(path, entry.Value, scalars, stack);
            }

            int index = 0;
            foreach (PzTableEntry entry in frame.Table.PositionalEntries)
            {
                Visit($"{frame.Prefix}[{index}]", entry.Value, scalars, stack);
                index++;
            }
        }

        return scalars;
    }

    private static void Visit(string path, PzValue value, List<PzScalarEntry> scalars, Stack<Frame> stack)
    {
        if (value is PzTable table)
        {
            stack.Push(new Frame(path, table));
        }
        else
        {
            scalars.Add(new PzScalarEntry(path, value));
        }
    }

    // Value-based, not lexeme-based: 1.0 and 1.00 encode the same, but an integer and a float of the
    // same magnitude do not (matching PzValueDiff's equality).
    private static string Encode(PzValue value) => value switch
    {
        PzBoolean b => b.Value ? "b:true" : "b:false",
        PzNumber n => $"n:{n.Value.ToString("R", CultureInfo.InvariantCulture)}:{(n.IsInteger ? "i" : "f")}",
        PzString s => $"s:{s.Value}",
        _ => throw new ArgumentException($"A {value.GetType().Name} is not a scalar leaf.", nameof(value)),
    };

    private readonly record struct Frame(string Prefix, PzTable Table);
}
