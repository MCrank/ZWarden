using System.Text.Json;
using ZWarden.Contracts.Protocol;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.LogStreaming;

/// <summary>
/// Splits one flush of log lines into <see cref="ServerLogBatch"/> messages that each serialize under
/// <see cref="AgentHubProtocol.StreamedMessageBudgetBytes"/> (#232). A flush is bounded by line count, not bytes, and
/// the protocol JSON escapes HTML-sensitive and non-ASCII characters to six bytes each, so a PZ start/stop burst could
/// exceed the hub's receive limit in one message — which closed the Agent's connection. Each line is measured as it
/// will be serialized; order is preserved and the "dropped" flag rides only the first part.
/// </summary>
internal static class ServerLogBatchSplitter
{
    // Room for the envelope around the lines (ids, timestamp, type, serverId, flags) and SignalR's invocation framing.
    private const int EnvelopeOverheadBytes = 1024;

    public static IReadOnlyList<ServerLogBatch> Split(ServerId serverId, IReadOnlyList<ServerLogLine> lines, bool dropped)
    {
        ArgumentNullException.ThrowIfNull(lines);

        int budget = AgentHubProtocol.StreamedMessageBudgetBytes - EnvelopeOverheadBytes;
        var parts = new List<ServerLogBatch>();
        var current = new List<ServerLogLine>();
        int currentBytes = 0;

        foreach (ServerLogLine line in lines)
        {
            // +1 for the separating comma. A single line always fits (sanitized lines are capped well under budget).
            int size = JsonSerializer.SerializeToUtf8Bytes(line, ProtocolJson.Options).Length + 1;
            if (current.Count > 0 && currentBytes + size > budget)
            {
                parts.Add(new ServerLogBatch(serverId, current, Dropped: parts.Count == 0 && dropped));
                current = [];
                currentBytes = 0;
            }

            current.Add(line);
            currentBytes += size;
        }

        if (current.Count > 0 || parts.Count == 0)
        {
            parts.Add(new ServerLogBatch(serverId, current, Dropped: parts.Count == 0 && dropped));
        }

        return parts;
    }
}
