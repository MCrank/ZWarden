using System.Globalization;
using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Validation;

/// <summary>
/// The default validator. Sandbox and INI are validated against a <see cref="PzSchema"/> (type, range,
/// unknown-key info); the two spawn files are validated <em>structurally</em>, since their shape — not a
/// key-range table — is what makes them valid. Every walk is iterative (trust-boundaries §8).
/// </summary>
public sealed class PzConfigValidator : IPzConfigValidator
{
    /// <inheritdoc/>
    public IReadOnlyList<PzConfigDiagnostic> Validate(IPzConfigDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<PzConfigDiagnostic>();

        switch (document.Kind)
        {
            case PzConfigKind.SandboxVars:
                if (!document.Root.TryGet("VERSION", out _))
                {
                    diagnostics.Add(new PzConfigDiagnostic(
                        PzDiagnosticSeverity.Error,
                        PzConfigDiagnostic.Codes.Missing,
                        "The sandbox file is missing the required VERSION key."));
                }

                ValidateAgainstSchema(document.Root, PzSchema.SandboxVars, coerceFromString: false, diagnostics);
                break;

            case PzConfigKind.Ini:
                ValidateAgainstSchema(document.Root, PzSchema.Ini, coerceFromString: true, diagnostics);
                break;

            case PzConfigKind.SpawnRegions:
                ValidateSpawnRegions(document.Root, diagnostics);
                break;

            case PzConfigKind.SpawnPoints:
                ValidateSpawnPoints(document.Root, diagnostics);
                break;

            default:
                break;
        }

        return diagnostics;
    }

    /// <summary>
    /// Checks one edit's wire-form value against the kind's schema before it is written (#223): the first
    /// type or range error, or <see langword="null"/> when the value fits or the key has no schema entry (an
    /// unknown key is applied as-is, ADR 0010). The value is read as text, the way the edit travels.
    /// </summary>
    public static PzConfigDiagnostic? ValidateEdit(PzConfigKind kind, string path, string value)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(value);

        if (PzSchema.For(kind) is not { } schema || !schema.TryGet(path, out PzSchemaEntry rule))
        {
            return null;
        }

        var diagnostics = new List<PzConfigDiagnostic>();
        ValidateScalar(path, new PzString(value), rule, coerceFromString: true, diagnostics);
        return diagnostics.Count == 0 ? null : diagnostics[0];
    }

    // Breadth-first over the named entries, building dotted paths, with no recursion. An unknown key is
    // informational and its subtree is not descended (one note per unknown container, not per leaf).
    private static void ValidateAgainstSchema(PzTable root, PzSchema schema, bool coerceFromString, List<PzConfigDiagnostic> diagnostics)
    {
        var queue = new Queue<(PzTable Table, string Prefix)>();
        queue.Enqueue((root, string.Empty));

        while (queue.Count > 0)
        {
            (PzTable table, string prefix) = queue.Dequeue();
            foreach (PzTableEntry entry in table.NamedEntries)
            {
                string path = prefix.Length == 0 ? entry.Key!.Name : $"{prefix}.{entry.Key!.Name}";

                if (!schema.TryGet(path, out PzSchemaEntry rule))
                {
                    diagnostics.Add(new PzConfigDiagnostic(
                        PzDiagnosticSeverity.Info,
                        PzConfigDiagnostic.Codes.UnknownKey,
                        $"'{path}' has no schema entry; it is preserved but not validated."));
                    continue;
                }

                if (rule.Type == PzValueType.Table)
                {
                    if (entry.Value is PzTable child)
                    {
                        queue.Enqueue((child, path));
                    }
                    else
                    {
                        diagnostics.Add(WrongType(path, "a table"));
                    }

                    continue;
                }

                ValidateScalar(path, entry.Value, rule, coerceFromString, diagnostics);
            }
        }
    }

    private static void ValidateScalar(string path, PzValue value, PzSchemaEntry rule, bool coerceFromString, List<PzConfigDiagnostic> diagnostics)
    {
        switch (rule.Type)
        {
            case PzValueType.Boolean:
                if (!TryReadBool(value, coerceFromString, out _))
                {
                    diagnostics.Add(WrongType(path, "a boolean"));
                }

                break;

            case PzValueType.Whole:
                if (!TryReadNumber(value, coerceFromString, out double intValue))
                {
                    diagnostics.Add(WrongType(path, "an integer"));
                }
                else if (intValue != Math.Truncate(intValue))
                {
                    diagnostics.Add(WrongType(path, "an integer"));
                }
                else
                {
                    CheckRange(path, intValue, rule, diagnostics);
                }

                break;

            case PzValueType.Number:
                if (!TryReadNumber(value, coerceFromString, out double numberValue))
                {
                    diagnostics.Add(WrongType(path, "a number"));
                }
                else
                {
                    CheckRange(path, numberValue, rule, diagnostics);
                }

                break;

            case PzValueType.Text:
                if (value is not PzString)
                {
                    diagnostics.Add(WrongType(path, "a string"));
                }

                break;

            case PzValueType.Table:
                diagnostics.Add(WrongType(path, "a scalar"));
                break;

            default:
                break;
        }
    }

    private static void CheckRange(string path, double value, PzSchemaEntry rule, List<PzConfigDiagnostic> diagnostics)
    {
        if ((rule.Min is { } min && value < min) || (rule.Max is { } max && value > max))
        {
            string range = (rule.Min, rule.Max) switch
            {
                ({ } lo, { } hi) => $"{Format(lo)}..{Format(hi)}",
                ({ } lo, null) => $"≥ {Format(lo)}",
                (null, { } hi) => $"≤ {Format(hi)}",
                _ => "its range",
            };

            diagnostics.Add(new PzConfigDiagnostic(
                PzDiagnosticSeverity.Error,
                PzConfigDiagnostic.Codes.OutOfRange,
                $"'{path}' is {Format(value)}, outside the allowed range ({range})."));
        }
    }

    private static void ValidateSpawnRegions(PzTable root, List<PzConfigDiagnostic> diagnostics)
    {
        int index = 0;
        foreach (PzTableEntry entry in root.PositionalEntries)
        {
            string where = $"region #{++index}";
            if (entry.Value is not PzTable region)
            {
                diagnostics.Add(new PzConfigDiagnostic(PzDiagnosticSeverity.Error, PzConfigDiagnostic.Codes.WrongType, $"Spawn {where} is not a table."));
                continue;
            }

            if (!(region.TryGet("name", out PzValue name) && name is PzString))
            {
                diagnostics.Add(new PzConfigDiagnostic(PzDiagnosticSeverity.Error, PzConfigDiagnostic.Codes.Missing, $"Spawn {where} is missing a string 'name'."));
            }

            bool hasFile = region.TryGet("file", out PzValue file) && file is PzString;
            bool hasServerFile = region.TryGet("serverfile", out PzValue serverFile) && serverFile is PzString;
            if (!hasFile && !hasServerFile)
            {
                diagnostics.Add(new PzConfigDiagnostic(PzDiagnosticSeverity.Error, PzConfigDiagnostic.Codes.Missing, $"Spawn {where} needs a string 'file' or 'serverfile'."));
            }
        }
    }

    private static void ValidateSpawnPoints(PzTable root, List<PzConfigDiagnostic> diagnostics)
    {
        foreach (PzTableEntry profession in root.NamedEntries)
        {
            string name = profession.Key!.Name;
            if (profession.Value is not PzTable cells)
            {
                diagnostics.Add(new PzConfigDiagnostic(PzDiagnosticSeverity.Error, PzConfigDiagnostic.Codes.WrongType, $"Spawn profession '{name}' is not a table of cells."));
                continue;
            }

            int index = 0;
            foreach (PzTableEntry cellEntry in cells.PositionalEntries)
            {
                string where = $"'{name}' cell #{++index}";
                if (cellEntry.Value is not PzTable cell)
                {
                    diagnostics.Add(new PzConfigDiagnostic(PzDiagnosticSeverity.Error, PzConfigDiagnostic.Codes.WrongType, $"Spawn {where} is not a table."));
                    continue;
                }

                bool hasX = cell.TryGet("posX", out PzValue x) && x is PzNumber;
                bool hasY = cell.TryGet("posY", out PzValue y) && y is PzNumber;
                if (!hasX || !hasY)
                {
                    diagnostics.Add(new PzConfigDiagnostic(PzDiagnosticSeverity.Error, PzConfigDiagnostic.Codes.Missing, $"Spawn {where} needs numeric posX and posY."));
                }
            }
        }
    }

    private static bool TryReadBool(PzValue value, bool coerceFromString, out bool result)
    {
        switch (value)
        {
            case PzBoolean b:
                result = b.Value;
                return true;
            case PzString s when coerceFromString && bool.TryParse(s.Value, out result):
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static bool TryReadNumber(PzValue value, bool coerceFromString, out double result)
    {
        switch (value)
        {
            case PzNumber n:
                result = n.Value;
                return true;
            case PzString s when coerceFromString && double.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out result):
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static PzConfigDiagnostic WrongType(string path, string expected) =>
        new(PzDiagnosticSeverity.Error, PzConfigDiagnostic.Codes.WrongType, $"'{path}' is not {expected}.");

    private static string Format(double value) =>
        value == Math.Truncate(value) && !double.IsInfinity(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
}
