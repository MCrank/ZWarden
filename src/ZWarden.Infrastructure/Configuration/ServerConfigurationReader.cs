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
/// schema description first, else the file comment, else none. A key with no schema entry falls into the "Other"
/// section with a comment-only tooltip and passes through unvalidated (F20a). The read persists nothing (ADR 0011).
/// </summary>
public sealed class ServerConfigurationReader : IServerConfigurationReader
{
    private const string OtherSection = "Other";

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
        PzSchema? schema = PzSchema.For(ToKind(file));

        // Group settings by section, first-appearance order, with "Other" (unknown keys) pinned last.
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

            string section = schemaEntry?.Section ?? OtherSection;
            string label = schemaEntry?.Label ?? setting.Path;

            var view = new ConfigSettingView(
                setting.Path,
                label,
                setting.Kind,
                ShapeOf(schemaEntry?.Type, setting.Kind, setting.Value),
                setting.Value,
                schemaEntry?.Min,
                schemaEntry?.Max,
                schemaEntry?.Default is { } def ? WireValueOf(def) : null,
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

        order.Sort(static (a, b) =>
            a == OtherSection ? (b == OtherSection ? 0 : 1) : (b == OtherSection ? -1 : 0));

        List<ConfigSection> sections = [.. order.Select(name => new ConfigSection(name, bySection[name]))];
        return new ConfigDocumentView(
            ConfigReadOutcome.Read, sections, transfer.RawText, transfer.BaselineHash,
            MapDiagnostics(transfer.Diagnostics), null);
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
