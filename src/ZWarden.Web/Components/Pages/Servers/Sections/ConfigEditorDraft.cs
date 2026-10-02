using ZWarden.Application.Configuration;
using ZWarden.Domain.Configuration;

namespace ZWarden.Web.Components.Pages.Servers.Sections;

/// <summary>
/// The configuration editor's unsaved state for one file (#299, decision D1): the live read the rows came from, the
/// drift baseline, every row with its edit, and the raw-edit text. The Config section keeps it in
/// <c>[PersistentState]</c>, so the prerender's read reaches the circuit without a second Agent round-trip, and the
/// operator's unsaved edits survive a dropped connection or an evicted circuit. It lives in server memory only
/// (never browser storage): the values include secrets such as <c>RCONPassword</c>.
/// </summary>
public sealed class ConfigEditorDraft
{
    /// <summary>The file these rows belong to — the file an apply targets.</summary>
    public PzConfigFile File { get; set; }

    /// <summary>The live read the rows were built from.</summary>
    public ConfigDocumentView View { get; set; } = ConfigDocumentView.OfOutcome(ConfigReadOutcome.Read);

    /// <summary>The canonical value hash of the state the rows were rendered from (ADR 0042): sent back on apply so
    /// the Agent drift-checks against what the operator saw, and compared with a fresh read first.</summary>
    public string? BaselineHash { get; set; }

    /// <summary>One row per rendered setting, in render order.</summary>
    public List<ConfigEditorRow> Rows { get; set; } = [];

    /// <summary>The raw whole-file edit's text; <see langword="null"/> until the operator edits it.</summary>
    public string? RawContent { get; set; }

    /// <summary>The raw edit's acknowledgement (it replaces the whole file and can stop the server on start).</summary>
    public bool RawConfirmed { get; set; }

    /// <summary>The draft for a fresh read of <paramref name="file"/>: every row at its read value.</summary>
    public static ConfigEditorDraft From(PzConfigFile file, ConfigDocumentView view) => new()
    {
        File = file,
        View = view,
        BaselineHash = view.IsRead ? view.BaselineHash : null,
        Rows = BuildRows(view),
    };

    /// <summary>Whether any row differs from the value it was read with.</summary>
    public bool HasEdits => Rows.Any(r => r.IsDirty);

    /// <summary>The changed rows as surgical edits.</summary>
    public List<ConfigApplyEdit> Edits() =>
        [.. Rows.Where(r => r.IsDirty && !string.IsNullOrEmpty(r.Path)).Select(r => new ConfigApplyEdit(r.Path!, r.Kind, r.Current))];

    /// <summary>Carries <paramref name="edits"/> onto this draft's rows (whose Original is the current host value), so
    /// they show as changed and a second Apply writes them on top (#226).</summary>
    public void Carry(IEnumerable<ConfigApplyEdit> edits)
    {
        Dictionary<string, string> byPath = edits
            .GroupBy(e => e.Path, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Value, StringComparer.Ordinal);
        foreach (ConfigEditorRow row in Rows)
        {
            if (row.Path is null || !byPath.TryGetValue(row.Path, out string? value))
            {
                continue;
            }

            if (row.Toggle)
            {
                row.Flag = string.Equals(value, "true", StringComparison.Ordinal);
            }
            else
            {
                row.Value = value;
            }
        }
    }

    // Flattens the view's sections into one row per setting, in render order.
    private static List<ConfigEditorRow> BuildRows(ConfigDocumentView view)
    {
        List<ConfigEditorRow> rows = [];
        if (!view.IsRead)
        {
            return rows;
        }

        foreach (ConfigSection section in view.Sections)
        {
            foreach (ConfigSettingView setting in section.Settings)
            {
                // A choice list wins over the toggle, so only a Boolean with no options renders the switch.
                bool toggle = setting.Shape == ConfigValueShape.Boolean && setting.Options.Count == 0;
                rows.Add(new ConfigEditorRow
                {
                    Path = setting.Path,
                    Kind = setting.Kind,
                    Original = setting.Value,
                    Value = setting.Value,
                    Toggle = toggle,
                    Flag = toggle && string.Equals(setting.Value, "true", StringComparison.Ordinal),
                });
            }
        }

        return rows;
    }
}

/// <summary>One setting in the editor: what it was read as and what the operator has made of it.</summary>
public sealed class ConfigEditorRow
{
    /// <summary>The dotted path of the scalar.</summary>
    public string? Path { get; set; }

    /// <summary>How the value is interpreted on apply.</summary>
    public ConfigEditKind Kind { get; set; }

    /// <summary>The value the row was read with.</summary>
    public string? Original { get; set; }

    /// <summary>The edited value for text, number and choice-list controls.</summary>
    public string? Value { get; set; }

    /// <summary>The edited value for a boolean toggle.</summary>
    public bool Flag { get; set; }

    /// <summary>Whether this row renders the boolean toggle, so its value is <see cref="Flag"/> (#223).</summary>
    public bool Toggle { get; set; }

    /// <summary>The row's value as the apply sends it.</summary>
    public string Current => Toggle ? (Flag ? "true" : "false") : Value ?? string.Empty;

    /// <summary>Whether the row differs from the value it was read with.</summary>
    public bool IsDirty => !string.Equals(Current, Original ?? string.Empty, StringComparison.Ordinal);
}
