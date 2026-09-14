using ZWarden.Agent.ServerConfig;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.ControlPlane;

/// <summary>A configurable <see cref="IServerConfigWriter"/> double: records the call and returns a primed
/// outcome, so the command processor's config-apply case is tested without any filesystem or parser.</summary>
internal sealed class FakeServerConfigWriter : IServerConfigWriter
{
    public ConfigApplyOutcome Outcome { get; set; } = ConfigApplyOutcome.Applied("[[\"Zombies\",\"n:1:i\"]]", "hash-1", 1);

    public int ApplyCount { get; private set; }

    public ServerId? LastServerId { get; private set; }

    public PzConfigFile? LastFile { get; private set; }

    public string? LastBaselineHash { get; private set; }

    public IReadOnlyList<ConfigValueEdit>? LastEdits { get; private set; }

    public Task<ConfigApplyOutcome> ApplyAsync(
        ServerId serverId,
        PzConfigFile file,
        string? baselineHash,
        IReadOnlyList<ConfigValueEdit> edits,
        CancellationToken cancellationToken)
    {
        ApplyCount++;
        LastServerId = serverId;
        LastFile = file;
        LastBaselineHash = baselineHash;
        LastEdits = edits;
        return Task.FromResult(Outcome);
    }
}
