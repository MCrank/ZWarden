using ZWarden.Application.Configuration;
using ZWarden.Domain.Configuration;

namespace ZWarden.Web.Components.Pages.Servers.Sections;

/// <summary>
/// The configuration editor's unsaved state for one file (#299, decision D1): the live read the rows came from, the
/// drift baseline, every row with its edit, and the raw-edit text. It lives in circuit memory only (never browser
/// storage): the values include secrets such as <c>RCONPassword</c>. What survives a paused or evicted circuit is its
/// <see cref="ConfigDraftSnapshot"/>, only the unsaved edits (#322).
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

    /// <summary>The draft rebuilt over a fresh read after a paused or evicted circuit (#322): the snapshot's edits on
    /// top of the current values, against the baseline the operator was shown, so an apply still drift-checks what
    /// they saw.</summary>
    public static ConfigEditorDraft Restore(ConfigDraftSnapshot snapshot, ConfigDocumentView view)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ConfigEditorDraft draft = From(snapshot.File, view);
        draft.BaselineHash = snapshot.BaselineHash;
        draft.Carry(snapshot.Edits);
        draft.RawContent = snapshot.RawContent;
        draft.RawConfirmed = snapshot.RawConfirmed;
        return draft;
    }

    /// <summary>Whether any row differs from the value it was read with.</summary>
    public bool HasEdits => Rows.Any(r => r.IsDirty);

    /// <summary>Whether there is anything to lose: a changed row or a started raw edit.</summary>
    public bool HasUnsavedState => HasEdits || RawContent is not null || RawConfirmed;

    /// <summary>The unsaved edits alone, small enough to persist (#322); <see langword="null"/> when there are none.
    /// The whole draft (the read, every row, the raw text) is far past the Blazor hub's 32 KB receive limit on a real
    /// SandboxVars, and the circuit can read the file again.</summary>
    public ConfigDraftSnapshot? ToSnapshot() => HasUnsavedState
        ? new ConfigDraftSnapshot(File, BaselineHash, Edits(), RawContent, RawConfirmed)
        : null;

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

/// <summary>
/// What a paused or evicted circuit keeps of the editor (#299 D1, #322): the file, the baseline the operator was
/// shown, the changed settings and the raw edit. The rest is read again from the host on restore.
/// </summary>
public sealed record ConfigDraftSnapshot(
    PzConfigFile File,
    string? BaselineHash,
    List<ConfigApplyEdit> Edits,
    string? RawContent,
    bool RawConfirmed);

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
