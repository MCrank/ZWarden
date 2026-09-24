using ZWarden.Application.Authorization;
using ZWarden.Application.Configuration;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Servers;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Model;
using ZWarden.PzConfig.Validation;

namespace ZWarden.Infrastructure.Configuration;

/// <summary>
/// The tenant-scoped Configuration View reader (F20c, ADR 0041): the read sibling of
/// <see cref="ServerConfigurationEditor"/>. Fail-closed (ADR 0018): it resolves the Server through the tenant filter
/// (a foreign/unknown Server is <see cref="ConfigReadOutcome.ServerNotFound"/>) and authorizes
/// <c>ServerConfigurationEdit</c> against that specific Server before reading anything — matching the sibling
/// <see cref="ServerConfigurationHistory"/> read seam. It then asks the Server's owning Agent for the live file over
/// the non-Operation read channel and, on a successful read, overlays the ZWarden schema (friendly label, section,
/// widget shape, range, default) and turns each setting's raw harvested comment into a sanitized Setting Tooltip —
/// schema description first, else the file comment, else none. Sections and labels come from the setting catalog
/// (#227) — the in-game grouping, name-prefix rules, a mod's own table — so only a truly unmatched key lands in
/// "Other"; a key with no schema entry keeps its file-comment range and passes through unvalidated (F20a). The read
/// persists nothing (ADR 0011).
/// </summary>
public sealed class ServerConfigurationReader : IServerConfigurationReader
{
    // Key-name fragments marking an INI value as free text whatever it currently looks like (#223).
    private static readonly string[] FreeTextKeyHints = ["Password", "Token", "Secret", "Name", "Message", "Description"];

    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly IServerConfigReadChannel _channel;

    public ServerConfigurationReader(
        ServerRepository servers, IPermissionChecker permissions, IServerConfigReadChannel channel)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(channel);
        _servers = servers;
        _permissions = permissions;
        _channel = channel;
    }

    /// <inheritdoc />
    public async Task<ConfigDocumentView> ReadAsync(
        UserId user, ServerId server, PzConfigFile file, CancellationToken cancellationToken = default)
    {
        // Resolve first, through the tenant filter: an unknown or foreign-tenant Server is ServerNotFound and gives
        // the server-scoped authorization a concrete resource to check.
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return ConfigDocumentView.OfOutcome(ConfigReadOutcome.ServerNotFound);
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(user, Permissions.ServerConfigurationEdit, server: server, cancellationToken)
            .ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return ConfigDocumentView.OfOutcome(ConfigReadOutcome.NotAuthorized);
        }

        ConfigReadTransfer transfer = await _channel
            .ReadAsync(server, resolved.AgentId, file, cancellationToken).ConfigureAwait(false);

        return transfer.Status switch
        {
            ConfigTransferStatus.AgentOffline => ConfigDocumentView.OfOutcome(ConfigReadOutcome.AgentOffline),
            ConfigTransferStatus.TimedOut => ConfigDocumentView.OfOutcome(ConfigReadOutcome.TimedOut),
            ConfigTransferStatus.FileMissing => ConfigDocumentView.OfOutcome(ConfigReadOutcome.FileMissing),
            ConfigTransferStatus.ParseFailed => new ConfigDocumentView(
                ConfigReadOutcome.ParseFailed, [], transfer.RawText, null, MapDiagnostics(transfer.Diagnostics), null),
            _ => BuildView(file, transfer),
        };
    }

    private static ConfigDocumentView BuildView(PzConfigFile file, ConfigReadTransfer transfer)
    {
        PzConfigKind configKind = ToKind(file);
        PzSchema? schema = PzSchema.For(configKind);

        // Group settings by section; the sections are ordered once every setting is placed.
        var order = new List<string>();
        var bySection = new Dictionary<string, List<ConfigSettingView>>(StringComparer.Ordinal);

        foreach (ConfigTransferSetting setting in transfer.Settings)
        {
            PzSchemaEntry? schemaEntry = null;
            if (schema is not null && schema.TryGet(setting.Path, out PzSchemaEntry found))
            {
                schemaEntry = found;
            }

            PzSettingHelp? help = setting.Comment is { Length: > 0 }
                ? PzCommentSanitizer.Sanitize(setting.Comment)
                : null;

            string? tooltip = FirstNonEmpty(schemaEntry?.Description, help?.Text);
            IReadOnlyList<ConfigOption> options = help is { Options.Count: > 0 }
                ? [.. help.Options.Select(o => new ConfigOption(o.Value, o.Label))]
                : [];

            ConfigEditKind kind = file == PzConfigFile.Ini
                ? IniKindOf(setting.Path, setting.Value, schemaEntry?.Type)
                : setting.Kind;
            ConfigValueShape shape = ShapeOf(schemaEntry?.Type, kind, setting.Value);

            // A boolean key whose current value is not a boolean (e.g. an INI already damaged to "PVP=", #223) is
            // offered as an On/Off choice rather than a toggle: the choice keeps the current value selected, so
            // it only changes when the operator picks one. A toggle would read it as Off and an unrelated apply
            // would silently write false.
            if (shape == ConfigValueShape.Boolean && options.Count == 0 && !IsBoolean(setting.Value))
            {
                options = [new ConfigOption("true", "On"), new ConfigOption("false", "Off")];
            }

            // Sections and labels come from the setting catalog (#227) unless the schema overrides them; the range and
            // default fall back to the file's own "Min: … Max: … Default: …" comment phrase.
            string section = schemaEntry?.Section ?? PzSettingCatalog.SectionOf(configKind, setting.Path);
            string label = schemaEntry?.Label ?? PzSettingCatalog.LabelOf(setting.Path);
            string? defaultValue = schemaEntry?.Default is { } def ? WireValueOf(def) : help?.Default;

            var view = new ConfigSettingView(
                setting.Path,
                label,
                kind,
                shape,
                setting.Value,
                schemaEntry?.Min ?? help?.Min,
                schemaEntry?.Max ?? help?.Max,
                defaultValue,
                tooltip,
                options,
                schemaEntry is not null);

            if (!bySection.TryGetValue(section, out List<ConfigSettingView>? list))
            {
                list = [];
                bySection[section] = list;
                order.Add(section);
            }

            list.Add(view);
        }

        // The game's section order, then mod sections in file order (a stable sort), then "Other".
        List<ConfigSection> sections =
        [
            .. order
                .OrderBy(name => PzSettingCatalog.SectionRank(configKind, name))
                .Select(name => new ConfigSection(name, bySection[name])),
        ];
        return new ConfigDocumentView(
            ConfigReadOutcome.Read, sections, transfer.RawText, transfer.BaselineHash,
            MapDiagnostics(transfer.Diagnostics), null);
    }

    // The INI has no value types, so the Agent reports every INI value as Text (#223). Re-type it for the editor:
    // the schema's type when the key is known, else what the value reads as — a bare true/false is a boolean and an
    // integer/decimal string a number (#227). Credential and free-text keys are never inferred, so a numeric
    // password or a server named "true" stays a text box. The write side renders Bool/Number back to the same text.
    private static ConfigEditKind IniKindOf(string path, string value, PzValueType? schemaType)
    {
        switch (schemaType)
        {
            case PzValueType.Boolean:
                return ConfigEditKind.Bool;
            case PzValueType.Whole or PzValueType.Number:
                return ConfigEditKind.Number;
            case PzValueType.Text or PzValueType.Table:
                return ConfigEditKind.Text;
            default:
                break;
        }

        if (FreeTextKeyHints.Any(hint => path.Contains(hint, StringComparison.OrdinalIgnoreCase)))
        {
            return ConfigEditKind.Text;
        }

        if (IsBoolean(value))
        {
            return ConfigEditKind.Bool;
        }

        return IsIniNumber(value) ? ConfigEditKind.Number : ConfigEditKind.Text;
    }

    // PZ writes INI booleans as lowercase keywords; only those exact forms are toggles, so a toggle round-trips the
    // value it was rendered from without a spurious edit.
    private static bool IsBoolean(string value) => value is "true" or "false";

    // An optionally signed integer or plain decimal (PZ writes 6 and 1.0) — no exponent, hex or padding.
    private static bool IsIniNumber(string value)
    {
        ReadOnlySpan<char> digits = value.AsSpan();
        if (digits.Length > 0 && digits[0] == '-')
        {
            digits = digits[1..];
        }

        int dot = digits.IndexOf('.');
        ReadOnlySpan<char> whole = dot < 0 ? digits : digits[..dot];
        ReadOnlySpan<char> fraction = dot < 0 ? [] : digits[(dot + 1)..];
        return whole.Length > 0
            && !whole.ContainsAnyExceptInRange('0', '9')
            && (dot < 0 || (fraction.Length > 0 && !fraction.ContainsAnyExceptInRange('0', '9')));
    }

    private static string? FirstNonEmpty(string? preferred, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            return preferred;
        }

        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }

    private static ConfigValueShape ShapeOf(PzValueType? schemaType, ConfigEditKind kind, string value)
    {
        if (schemaType is { } type)
        {
            switch (type)
            {
                case PzValueType.Boolean:
                    return ConfigValueShape.Boolean;
                case PzValueType.Whole:
                    return ConfigValueShape.Whole;
                case PzValueType.Number:
                    return ConfigValueShape.Fractional;
                case PzValueType.Text:
                    return ConfigValueShape.Text;
                default:
                    break; // Table has no scalar widget; fall through to the value-derived shape.
            }
        }

        // No schema: derive the shape from the current value. A number with no decimal point is a whole number
        // (PZ writes integers as 6 and doubles as 1.0 — research §2.1), so a stepper fits.
        return kind switch
        {
            ConfigEditKind.Bool => ConfigValueShape.Boolean,
            ConfigEditKind.Number => value.Contains('.', StringComparison.Ordinal)
                ? ConfigValueShape.Fractional
                : ConfigValueShape.Whole,
            _ => ConfigValueShape.Text,
        };
    }

    private static string WireValueOf(PzValue value) => value switch
    {
        PzBoolean b => b.Value ? "true" : "false",
        PzNumber n => n.Lexeme,
        PzString s => s.Value,
        _ => string.Empty,
    };

    private static IReadOnlyList<ConfigDiagnosticView> MapDiagnostics(IReadOnlyList<ConfigTransferDiagnostic> diagnostics) =>
        diagnostics.Count == 0
            ? []
            : [.. diagnostics.Select(d => new ConfigDiagnosticView(d.Message, d.Line, d.Column))];

    private static PzConfigKind ToKind(PzConfigFile file) => file switch
    {
        PzConfigFile.Ini => PzConfigKind.Ini,
        PzConfigFile.SandboxVars => PzConfigKind.SandboxVars,
        PzConfigFile.SpawnRegions => PzConfigKind.SpawnRegions,
        PzConfigFile.SpawnPoints => PzConfigKind.SpawnPoints,
        _ => throw new ArgumentOutOfRangeException(nameof(file), file, "Unknown configuration file."),
    };
}
