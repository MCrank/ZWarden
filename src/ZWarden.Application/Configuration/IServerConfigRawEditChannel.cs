using ZWarden.Domain.Ids;

namespace ZWarden.Application.Configuration;

/// <summary>
/// The transport seam for staging an operator-authored whole-file configuration edit to a Server's owning Agent
/// (F20c PR-D, ADR 0042) — the write-direction sibling of <see cref="IServerConfigReadChannel"/>. The file text is
/// too large for the 2 KB Operation command payload, so it is chunked and sent up its own channel <b>before</b> the
/// <c>ConfigApplyRaw</c> Operation is enqueued; the Agent holds it transiently, keyed by the correlation id this
/// returns, and applies it when the Operation runs. Implemented on ZWarden.Web (the tier that owns the Agent hub
/// connection); consumed by <see cref="IServerConfigurationEditor"/> before it enqueues the apply.
/// </summary>
public interface IServerConfigRawEditChannel
{
    /// <summary>Stages <paramref name="rawText"/> to <paramref name="owningAgent"/> for <paramref name="server"/>
    /// and returns the correlation id it was staged under, or an <see cref="ConfigRawEditStageStatus.AgentOffline"/>
    /// result at once when the Agent is not connected — never throws.</summary>
    Task<ConfigRawEditStage> StageAsync(
        ServerId server, AgentId owningAgent, string rawText, CancellationToken cancellationToken = default);
}

/// <summary>How staging a raw edit turned out at the transport layer (F20c).</summary>
public enum ConfigRawEditStageStatus
{
    /// <summary>The text was chunked and sent to the owning Agent's connection.</summary>
    Staged,

    /// <summary>The owning Agent is not connected, so there was nothing to stage to.</summary>
    AgentOffline,
}

/// <summary>The result of staging a raw edit: the <see cref="Status"/> and, when
/// <see cref="ConfigRawEditStageStatus.Staged"/>, the <see cref="CorrelationId"/> the Agent will retrieve the text
/// by when the <c>ConfigApplyRaw</c> Operation runs.</summary>
public sealed record ConfigRawEditStage(ConfigRawEditStageStatus Status, string? CorrelationId)
{
    /// <summary>The Agent was offline; nothing was staged.</summary>
    public static ConfigRawEditStage Offline() => new(ConfigRawEditStageStatus.AgentOffline, null);

    /// <summary>The text was staged under <paramref name="correlationId"/>.</summary>
    public static ConfigRawEditStage Ok(string correlationId) => new(ConfigRawEditStageStatus.Staged, correlationId);
}
