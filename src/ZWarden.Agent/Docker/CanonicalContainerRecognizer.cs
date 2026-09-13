using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Docker;

/// <summary>
/// Recognises a canonical ZWarden.PZServer container from its labels (PRD 25) and reads back the typed
/// <see cref="ServerId"/> and <see cref="AgentId"/> it was stamped with. Recognition is <b>all-or-nothing</b>:
/// the baked set (<see cref="CanonicalLabels.Managed"/>/<see cref="CanonicalLabels.Runtime"/>/
/// <see cref="CanonicalLabels.SchemaVersion"/>) must be present and exactly correct, and both ids must parse
/// to their canonical prefixes. Anything else — a foreign container, a mislabelled one, a wrong schema
/// version — is not ZWarden's, and the caller (<see cref="ContainerOwnershipGuard"/>) must refuse it.
/// </summary>
public static class CanonicalContainerRecognizer
{
    /// <summary>
    /// Attempts to recognise <paramref name="labels"/> as a canonical container and read its ids. Returns
    /// <c>false</c> — leaving both ids at their empty default — unless the whole baked set matches and both
    /// the <c>server-id</c> and <c>agent-id</c> labels parse. A <c>null</c> label map is treated as empty.
    /// </summary>
    public static bool TryRecognize(
        IReadOnlyDictionary<string, string>? labels,
        out ServerId serverId,
        out AgentId agentId)
    {
        serverId = default;
        agentId = default;

        if (labels is null
            || !HasValue(labels, CanonicalLabels.Managed, CanonicalLabels.ManagedValue)
            || !HasValue(labels, CanonicalLabels.Runtime, CanonicalLabels.RuntimeValue)
            || !HasValue(labels, CanonicalLabels.SchemaVersion, CanonicalLabels.SchemaVersionValue))
        {
            return false;
        }

        return labels.TryGetValue(CanonicalLabels.ServerId, out string? rawServerId)
            && Domain.Ids.ServerId.TryParse(rawServerId, out serverId)
            && labels.TryGetValue(CanonicalLabels.AgentId, out string? rawAgentId)
            && Domain.Ids.AgentId.TryParse(rawAgentId, out agentId);
    }

    private static bool HasValue(IReadOnlyDictionary<string, string> labels, string key, string expected) =>
        labels.TryGetValue(key, out string? actual)
        && string.Equals(actual, expected, StringComparison.Ordinal);
}
