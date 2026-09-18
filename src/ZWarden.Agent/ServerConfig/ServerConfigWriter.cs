using System.Diagnostics.CodeAnalysis;
using System.Globalization;
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
/// Applies surgical value edits to one of a Server's four Project Zomboid config files on the host <c>/pz/</c>
/// mount (F20b PR-3, ADR 0010/0011). The Agent owns <c>DataMountRoot</c>, so it reads and writes the file
/// directly — the host-side sibling of the container's <c>/pz/data/Server/&lt;name&gt;.*</c>.
/// </summary>
public interface IServerConfigWriter
{
    /// <summary>
    /// Re-reads and re-parses the live <paramref name="file"/> for <paramref name="serverId"/>, fails the write
    /// <b>closed</b> if the file drifted from <paramref name="baselineHash"/> (a second author changed it since
    /// the last recorded revision — ADR 0011), then applies each edit as a byte-preserving surgical replacement
    /// and writes the result back BOM-less through a temp-file-and-atomic-replace. Returns a first-class
    /// <see cref="ConfigApplyOutcome"/> for every expected condition rather than throwing.
    /// </summary>
    Task<ConfigApplyOutcome> ApplyAsync(
        ServerId serverId,
        PzConfigFile file,
        string? baselineHash,
        IReadOnlyList<ConfigValueEdit> edits,
        CancellationToken cancellationToken);
}

/// <summary>
/// The default <see cref="IServerConfigWriter"/> over the Agent's <c>DataMountRoot</c>. The path mirrors the
/// container launch (F12/F17): the data volume is <c>&lt;root&gt;/&lt;serverId&gt;</c> (bound at <c>/pz/data</c>)
/// and PZ launches with <c>-servername servertest</c>, so the four files live under
/// <c>&lt;root&gt;/&lt;serverId&gt;/Server/servertest*</c>. Parsing and the value-tree drift check are delegated
/// to <see cref="ZWarden.PzConfig"/> behind its seam; the atomic BOM-less write mirrors the Agent's file stores
/// (a temp file in the same directory, then <see cref="File.Move(string,string,bool)"/>) — a torn write or a BOM
/// makes PZ exit on start with no self-healing path (ADR 0011).
/// </summary>
public sealed class ServerConfigWriter : IServerConfigWriter
{
    private readonly IPzConfigParser _parser;
    private readonly AgentOptions _options;

    public ServerConfigWriter(IPzConfigParser parser, IOptions<AgentOptions> options)
    {
        ArgumentNullException.ThrowIfNull(parser);
        ArgumentNullException.ThrowIfNull(options);
        _parser = parser;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<ConfigApplyOutcome> ApplyAsync(
        ServerId serverId,
        PzConfigFile file,
        string? baselineHash,
        IReadOnlyList<ConfigValueEdit> edits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edits);

        string path = ServerConfigFiles.PathFor(_options.DataMountRoot, serverId, file);
        if (!File.Exists(path))
        {
            return ConfigApplyOutcome.Failed(
                $"The {ServerConfigFiles.FileName(file)} configuration file does not exist for this server yet.");
        }

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigApplyOutcome.Failed($"Could not read the configuration file: {ex.Message}");
        }

        PzConfigReadResult read = _parser.Open(ServerConfigFiles.ToKind(file), bytes);
        if (!read.Parsed || read.Document is not { } document)
        {
            string detail = read.Diagnostics.Count > 0 ? read.Diagnostics[0].Message : "unknown error";
            return ConfigApplyOutcome.Failed($"The current configuration file did not parse: {detail}");
        }

        // Fail closed on drift (ADR 0011): compare the freshly-parsed live file against the recorded baseline
        // before touching it, so a change a second author (the in-game admin panel) made is never silently lost.
        PzDriftResult drift = PzDriftCheck.Compare(baselineHash, document);
        if (!drift.WriteAllowed)
        {
            return ConfigApplyOutcome.DriftRefused(
                "The configuration on disk changed outside ZWarden since the last recorded revision, so the write "
                + "was refused to avoid discarding that change. Reconcile the drift and try again.");
        }

        int applied = 0;
        foreach (ConfigValueEdit edit in edits)
        {
            if (!TryBuildValue(edit, out PzValue value, out string? valueError))
            {
                return ConfigApplyOutcome.Failed(valueError);
            }

            PzConfigEditResult result = document.TrySetValue(edit.Path, value);
            if (!result.Ok)
            {
                return ConfigApplyOutcome.Failed($"Could not apply the edit to '{edit.Path}': {result.Message}");
            }

            applied++;
        }

        try
        {
            await WriteAtomicAsync(path, document.Emit(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ConfigApplyOutcome.Failed($"Could not write the configuration file: {ex.Message}");
        }

        // The new baseline: the values the file holds after the write, order-normalized (ADR 0011).
        PzValueSnapshot snapshot = PzValueSnapshot.Of(document);
        return ConfigApplyOutcome.Applied(snapshot.CanonicalText, snapshot.Hash, applied);
    }

    // Reconstructs the parser's value node from the wire edit. The command is control-plane input, but the Agent
    // re-validates it defensively before writing (a malformed number or boolean is an actionable failure, never
    // a corrupt file).
    private static bool TryBuildValue(ConfigValueEdit edit, out PzValue value, [NotNullWhen(false)] out string? error)
    {
        switch (edit.Kind)
        {
            case ConfigValueKind.Bool:
                if (!bool.TryParse(edit.Value, out bool parsedBool))
                {
                    value = null!;
                    error = $"'{edit.Value}' is not a valid boolean for '{edit.Path}'.";
                    return false;
                }

                value = new PzBoolean(parsedBool);
                error = null;
                return true;

            case ConfigValueKind.Number:
                string lexeme = edit.Value.Trim();
                if (!double.TryParse(
                        lexeme,
                        NumberStyles.Float | NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out double parsedNumber))
                {
                    value = null!;
                    error = $"'{edit.Value}' is not a valid number for '{edit.Path}'.";
                    return false;
                }

                // PZ emits integers as 6 and doubles as 1.0 (research §2.1); the lexeme carries the shape.
                bool isInteger = lexeme.IndexOf('.') < 0
                    && lexeme.IndexOf('e', StringComparison.OrdinalIgnoreCase) < 0;
                value = new PzNumber(parsedNumber, lexeme, isInteger);
                error = null;
                return true;

            case ConfigValueKind.Text:
                value = new PzString(edit.Value);
                error = null;
                return true;

            default:
                value = null!;
                error = $"Unknown configuration value kind '{edit.Kind}' for '{edit.Path}'.";
                return false;
        }
    }

    // A temp file in the same directory, then an atomic replace — the Agent's file-store idiom (F8/F9). Emit()
    // already returns BOM-less bytes, so they are written verbatim.
    private static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        string tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
