using ZWarden.Agent.Diagnostics;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Diagnostics;

/// <summary>A host gatherer that returns a canned bundle and records that it ran.</summary>
internal sealed class FakeHostDiagnosticsGatherer : IHostDiagnosticsGatherer
{
    public HostDiagnosticsResult Result { get; set; } = new([]);

    public int Calls { get; private set; }

    public Task<HostDiagnosticsResult> GatherAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Result);
    }
}

/// <summary>A per-server gatherer that returns a canned bundle and records the ServerId it was asked about.</summary>
internal sealed class FakeServerDiagnosticsGatherer : IServerDiagnosticsGatherer
{
    public ServerDiagnosticsResult Result { get; set; } = new([]);

    public int Calls { get; private set; }

    public ServerId? LastServerId { get; private set; }

    public Task<ServerDiagnosticsResult> GatherAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        Calls++;
        LastServerId = serverId;
        return Task.FromResult(Result);
    }
}
