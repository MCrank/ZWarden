using System.Text;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Model;
using ZWarden.PzConfig.Revisions;

namespace ZWarden.Agent.ServerConfig;

/// <summary>
/// Reads one of a Server's four Project Zomboid config files live from the host <c>/pz/</c> mount and returns a
/// structured, layer-neutral view (F20c, ADR 0041) — the read sibling of <see cref="ServerConfigWriter"/>. It reads
/// the file once, parses it through the same <see cref="IPzConfigParser"/> seam (the untrusted Lua parse stays on
/// the Agent, ADR 0010), and emits the current scalar values, each setting's raw harvested comment, the whole raw
/// text (for the advanced raw view), the canonical value-snapshot hash (the drift baseline), and any parse
/// diagnostics. A missing or unparseable file is a first-class <see cref="ConfigReadStatus"/>, never an exception:
/// the read is a report, and a control-plane read must not be able to crash the Agent connection.
/// </summary>
public interface IServerConfigReader
{
    /// <summary>Reads and parses the live <paramref name="file"/> for <paramref name="serverId"/> and returns its
    /// structured view. Never throws for an expected condition (absent file, parse failure, unreadable file); those
    /// are carried on the returned <see cref="ConfigReadPayload.Status"/>.</summary>
    Task<ConfigReadPayload> ReadAsync(ServerId serverId, PzConfigFile file, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IServerConfigReader" />
public sealed class ServerConfigReader : IServerConfigReader
{
    // A harvested comment is locale-generated, attacker-influenced output (PRD 38): bound each one before it goes on
    // the wire so a pathological file cannot inflate the reply. The whole file is already size-capped by the parser
    // pre-check (ADR 0010), so the raw text needs no separate cap.
    private const int MaxCommentLength = 4_000;

    private readonly IPzConfigParser _parser;
    private readonly AgentOptions _options;

    public ServerConfigReader(IPzConfigParser parser, IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(options);
        _parser = parser;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ConfigReadPayload> ReadAsync(ServerId serverId, PzConfigFile file, CancellationToken cancellationToken)
    {
        string path = ServerConfigFiles.PathFor(_options.DataMountRoot, serverId, file);
        if (!File.Exists(path))
        {
            return new ConfigReadPayload(serverId, file, ConfigReadStatus.FileMissing, [], string.Empty, null, []);
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ConfigReadPayload(
                serverId, file, ConfigReadStatus.ParseFailed, [], string.Empty, null,
                [new ConfigReadDiagnostic($"Could not read the configuration file: {ex.Message}", null, null)]);
        }

        string rawText = DecodeText(bytes);
        PzConfigReadResult read = _parser.Open(ServerConfigFiles.ToKind(file), bytes);
        IReadOnlyList<ConfigReadDiagnostic> diagnostics = MapDiagnostics(read.Diagnostics);

        if (!read.Parsed || read.Document is not { } document)
        {
            // The file exists but did not parse: still return its raw text so the operator can see and fix it.
            return new ConfigReadPayload(serverId, file, ConfigReadStatus.ParseFailed, [], rawText, null, diagnostics);
        }

        PzValueSnapshot snapshot = PzValueSnapshot.Of(document);
        List<ConfigSettingValue> settings = [.. snapshot.Scalars.Select(s => new ConfigSettingValue(
            s.Path,
            KindOf(s.Value),
            WireValueOf(s.Value),
            read.Comments.TryGetValue(s.Path, out string? comment) ? Cap(comment) : null))];

        return new ConfigReadPayload(serverId, file, ConfigReadStatus.Read, settings, rawText, snapshot.Hash, diagnostics);
    }

    // Decode as UTF-8 and drop a leading BOM if the file carries one, so the raw view shows the text PZ sees. Writing
    // is BOM-less (ADR 0011); reading tolerates a BOM a foreign editor may have added.
    private static string DecodeText(byte[] bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);
        return text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
    }

    private static IReadOnlyList<ConfigReadDiagnostic> MapDiagnostics(IReadOnlyList<PzConfigDiagnostic> diagnostics) =>
        diagnostics.Count == 0
            ? []
            : [.. diagnostics.Select(d => new ConfigReadDiagnostic(d.Message, d.Position?.Line, d.Position?.Column))];

    private static string Cap(string comment) => comment.Length <= MaxCommentLength ? comment : comment[..MaxCommentLength];

    // The wire kind of the current value — the inverse of the writer's TryBuildValue, and the same mapping the F20b
    // editor uses when it turns a restore into edits.
    private static ConfigValueKind KindOf(PzValue value) => value switch
    {
        PzBoolean => ConfigValueKind.Bool,
        PzNumber => ConfigValueKind.Number,
        PzString => ConfigValueKind.Text,
        _ => throw new ArgumentException($"A {value.GetType().Name} is not a scalar value.", nameof(value)),
    };

    private static string WireValueOf(PzValue value) => value switch
    {
        PzBoolean b => b.Value ? "true" : "false",
        PzNumber n => n.Lexeme,
        PzString s => s.Value,
        _ => throw new ArgumentException($"A {value.GetType().Name} is not a scalar value.", nameof(value)),
    };
}
