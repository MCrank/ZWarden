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
        return FromScalars(Flatten(root));
    }

    /// <summary>
    /// Reconstructs a snapshot from the <see cref="CanonicalText"/> a <c>ConfigurationRevision</c> persisted
    /// (F20b PR-4). The control plane holds no live file to re-parse, so the history diff and a value-level
    /// restore read the stored snapshot back through here — the inverse of the private <see cref="Encode"/>.
    /// The result canonicalizes and hashes identically to the <see cref="Of(PzTable)"/> that produced the
    /// text, so a round-trip preserves <see cref="Hash"/>. Throws <see cref="FormatException"/> on text this
    /// library did not write.
    /// </summary>
    public static PzValueSnapshot Parse(string canonicalText)
    {
        ArgumentNullException.ThrowIfNull(canonicalText);

        string[][]? pairs;
        try
        {
            pairs = JsonSerializer.Deserialize<string[][]>(canonicalText);
        }
        catch (JsonException ex)
        {
            throw new FormatException("The canonical snapshot text is not valid snapshot JSON.", ex);
        }

        if (pairs is null)
        {
            throw new FormatException("The canonical snapshot text deserialized to null.");
        }

        var scalars = new List<PzScalarEntry>(pairs.Length);
        foreach (string[] pair in pairs)
        {
            if (pair is not [string path, string encoded])
            {
                throw new FormatException("A canonical snapshot entry is not a [path, value] pair.");
            }

            scalars.Add(new PzScalarEntry(path, Decode(encoded)));
        }

        return FromScalars(scalars);
    }

    private static PzValueSnapshot FromScalars(List<PzScalarEntry> scalars)
    {
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

    // The inverse of Encode. A number's original lexeme is not stored (the snapshot is value-level, ADR 0011),
    // so it is synthesized from the value and its integer-ness to the shape PZ writes: an integer has no
    // decimal point (6), a float always carries one (1.0) — the same signal the Agent reads back on the wire.
    private static PzValue Decode(string encoded)
    {
        if (encoded is "b:true")
        {
            return new PzBoolean(true);
        }

        if (encoded is "b:false")
        {
            return new PzBoolean(false);
        }

        if (encoded.StartsWith("s:", StringComparison.Ordinal))
        {
            return new PzString(encoded[2..]);
        }

        if (encoded.StartsWith("n:", StringComparison.Ordinal))
        {
            string[] parts = encoded.Split(':');
            if (parts.Length == 3
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                && parts[2] is "i" or "f")
            {
                bool isInteger = parts[2] is "i";
                string canonical = number.ToString("R", CultureInfo.InvariantCulture);
                string lexeme = isInteger || canonical.IndexOfAny(['.', 'e', 'E']) >= 0 ? canonical : canonical + ".0";
                return new PzNumber(number, lexeme, isInteger);
            }
        }

        throw new FormatException($"'{encoded}' is not a recognized canonical scalar encoding.");
    }

    private readonly record struct Frame(string Prefix, PzTable Table);
}
