using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Configuration;

/// <summary>
/// The operator-facing seam for a <b>Configuration View</b> — a live, non-mutating read of one of a Server's config
/// files, presented with current values and per-setting tooltips (F20c, ADR 0041). It resolves the Server through
/// the tenant filter and authorizes the server-scoped <c>ServerConfigurationEdit</c> permission fail-closed (ADR
/// 0018), matching the sibling <see cref="IServerConfigurationHistory"/>; it then reads the live file over the
/// non-Operation read channel (<see cref="IServerConfigReadChannel"/>), overlays the ZWarden schema (friendly label,
/// section, range, default) and sanitizes each setting's harvested comment into a <b>Setting Tooltip</b>. Every
/// expected condition — denied, agent offline, timed out, missing or unparseable file — is a first-class
/// <see cref="ConfigReadOutcome"/> on the returned view, not an exception. The read persists nothing (ADR 0011).
/// </summary>
public interface IServerConfigurationReader
{
    /// <summary>Reads the current <b>Configuration View</b> of <paramref name="file"/> for <paramref name="server"/>
    /// on behalf of <paramref name="user"/>. The returned view's <see cref="ConfigDocumentView.Outcome"/> tells the
    /// caller whether it was read, denied, or unavailable.</summary>
    Task<ConfigDocumentView> ReadAsync(
        UserId user, ServerId server, PzConfigFile file, CancellationToken cancellationToken = default);
}

/// <summary>How a <see cref="ConfigDocumentView"/> read turned out — an authorization or availability outcome, or a
/// successful read (F20c).</summary>
public enum ConfigReadOutcome
{
    /// <summary>The file was read; <see cref="ConfigDocumentView.Sections"/> and raw text are populated.</summary>
    Read,

    /// <summary>The caller lacks <c>ServerConfigurationEdit</c> on this Server.</summary>
    NotAuthorized,

    /// <summary>The Server is not visible to the caller's tenant, or does not exist.</summary>
    ServerNotFound,

    /// <summary>The Server's owning Agent is not connected.</summary>
    AgentOffline,

    /// <summary>The owning Agent is connected but did not reply in time.</summary>
    TimedOut,

    /// <summary>The file exists but did not parse; <see cref="ConfigDocumentView.Diagnostics"/> carries the reason
    /// and the raw text is still available for the raw view.</summary>
    ParseFailed,

    /// <summary>The file does not exist for this Server yet.</summary>
    FileMissing,
}

/// <summary>The widget-relevant shape of a setting's value, refined from the schema where one exists so the UI can
/// choose a control (a whole-number stepper vs. a decimal slider); falls back to the parsed value's shape for a key
/// the schema does not know (F20c).</summary>
public enum ConfigValueShape
{
    /// <summary>A boolean — rendered as a toggle.</summary>
    Boolean,

    /// <summary>An integer-valued number — a stepper or a whole-step slider.</summary>
    Whole,

    /// <summary>A fractional number — a slider or a decimal input.</summary>
    Fractional,

    /// <summary>Free text — an input.</summary>
    Text,
}

/// <summary>One selectable value for a ranged-enum setting (F20c): the wire <paramref name="Value"/> and its
/// human <paramref name="Label"/> (e.g. <c>"1"</c> → <c>"Sprinters"</c>), from the schema or the harvested comment.</summary>
public sealed record ConfigOption(string Value, string Label);

/// <summary>One diagnostic on a <see cref="ConfigDocumentView"/> (F20c), with a 1-based position where one applies.</summary>
public sealed record ConfigDiagnosticView(string Message, int? Line, int? Column);

/// <summary>
/// One setting in a <see cref="ConfigDocumentView"/> (F20c): its dotted <paramref name="Path"/>, a friendly
/// <paramref name="Label"/>, the current <paramref name="Value"/> in wire form (interpreted per
/// <paramref name="Kind"/>), the widget-relevant <paramref name="Shape"/> and numeric range/default from the schema,
/// a resolved <paramref name="Tooltip"/> (schema description, else the sanitized file comment, else none), and any
/// enum <paramref name="Options"/>. <paramref name="KnownToSchema"/> is false for a key ZWarden has no schema entry
/// for — it passes through unvalidated (F20a). <paramref name="Managed"/> marks a key ZWarden owns (the INI ports,
/// #228): the editor shows it read-only and every apply path refuses a change to it.
/// </summary>
public sealed record ConfigSettingView(
    string Path,
    string Label,
    ConfigEditKind Kind,
    ConfigValueShape Shape,
    string Value,
    double? Min,
    double? Max,
    string? Default,
    string? Tooltip,
    IReadOnlyList<ConfigOption> Options,
    bool KnownToSchema,
    bool Managed = false);

/// <summary>One named group of settings in a <see cref="ConfigDocumentView"/> (F20c) — a schema-authored section, or
/// the catch-all "Other" for keys with no schema entry.</summary>
public sealed record ConfigSection(string Name, IReadOnlyList<ConfigSettingView> Settings);

/// <summary>
/// The <b>Configuration View</b> (F20c, ADR 0041): the transient, structured result of a live read, its settings
/// grouped into <see cref="Sections"/> with current values and tooltips, plus the whole <see cref="RawText"/> for
/// the advanced raw view and the <see cref="BaselineHash"/> the editor sends back as the drift baseline on apply.
/// Nothing here is persisted; a Configuration Revision (F20b) remains the write path's durable record.
/// </summary>
public sealed record ConfigDocumentView(
    ConfigReadOutcome Outcome,
    IReadOnlyList<ConfigSection> Sections,
    string RawText,
    string? BaselineHash,
    IReadOnlyList<ConfigDiagnosticView> Diagnostics,
    string? Message)
{
    /// <summary><see langword="true"/> when the file was read (as opposed to denied or unavailable).</summary>
    public bool IsRead => Outcome == ConfigReadOutcome.Read;

    /// <summary>A view carrying only an outcome (denial or unavailability), with no content.</summary>
    public static ConfigDocumentView OfOutcome(ConfigReadOutcome outcome, string? message = null) =>
        new(outcome, [], string.Empty, null, [], message);
}
