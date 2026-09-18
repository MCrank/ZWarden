using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Configuration;

/// <summary>
/// The transport seam for a live configuration read (F20c, ADR 0041): given a Server, its owning Agent, and a file,
/// it asks that Agent to read the file and returns the reassembled, layer-neutral <see cref="ConfigReadTransfer"/>.
/// Implemented on ZWarden.Web (the tier that owns the Agent hub connection) as the correlating coordinator that
/// sends the request, reassembles the chunked reply, times out a silent Agent, and reports an offline one — the
/// read-path sibling of the F27 log-subscription coordinator. Consumed by <see cref="IServerConfigurationReader"/>,
/// which authorizes and overlays the schema on top. The read is transient: nothing is persisted (ADR 0011).
/// </summary>
public interface IServerConfigReadChannel
{
    /// <summary>Requests a read of <paramref name="file"/> for <paramref name="server"/> from
    /// <paramref name="owningAgent"/> and returns the reassembled view. Returns an
    /// <see cref="ConfigTransferStatus.AgentOffline"/> transfer at once when the Agent is not connected, and a
    /// <see cref="ConfigTransferStatus.TimedOut"/> one when it is connected but does not reply in time — never
    /// throws for either.</summary>
    Task<ConfigReadTransfer> ReadAsync(
        ServerId server, AgentId owningAgent, PzConfigFile file, CancellationToken cancellationToken = default);
}

/// <summary>How a live read turned out at the transport layer (F20c) — the Agent's own status widened with the two
/// transport outcomes only the control plane can observe.</summary>
public enum ConfigTransferStatus
{
    /// <summary>The Agent read and parsed the file.</summary>
    Read,

    /// <summary>The file exists but did not parse; <see cref="ConfigReadTransfer.Diagnostics"/> carries the reason.</summary>
    ParseFailed,

    /// <summary>The file does not exist for this Server yet.</summary>
    FileMissing,

    /// <summary>The owning Agent is not connected, so there was nothing to ask.</summary>
    AgentOffline,

    /// <summary>The owning Agent is connected but did not reply within the read timeout.</summary>
    TimedOut,
}

/// <summary>One scalar setting from a live read, layer-neutral (F20c): its dotted path, current value in wire form
/// interpreted per <paramref name="Kind"/>, and its raw harvested comment (or <see langword="null"/>).</summary>
public sealed record ConfigTransferSetting(string Path, ConfigEditKind Kind, string Value, string? Comment);

/// <summary>One diagnostic from a live read, layer-neutral (F20c), with a 1-based position where one applies.</summary>
public sealed record ConfigTransferDiagnostic(string Message, int? Line, int? Column);

/// <summary>
/// The reassembled, layer-neutral result of a live configuration read (F20c, ADR 0041) as it leaves the transport
/// seam — the current scalar <see cref="Settings"/>, the whole <see cref="RawText"/>, the drift
/// <see cref="BaselineHash"/>, and any <see cref="Diagnostics"/>. The schema overlay and the tenant authorization are
/// applied above this by <see cref="IServerConfigurationReader"/>; this carries no ZWarden presentation metadata.
/// </summary>
public sealed record ConfigReadTransfer(
    ConfigTransferStatus Status,
    IReadOnlyList<ConfigTransferSetting> Settings,
    string RawText,
    string? BaselineHash,
    IReadOnlyList<ConfigTransferDiagnostic> Diagnostics)
{
    /// <summary>A transport-only outcome (offline/timeout) carrying no content.</summary>
    public static ConfigReadTransfer OfStatus(ConfigTransferStatus status) =>
        new(status, [], string.Empty, null, []);
}
