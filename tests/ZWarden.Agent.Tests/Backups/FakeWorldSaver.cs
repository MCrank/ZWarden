using ZWarden.Agent.Backups;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Backups;

/// <summary>A <see cref="IWorldSaver"/> double: returns a preset result, counts calls, and writes "save" to an
/// optional shared journal so a test can prove the save happened before the archive.</summary>
internal sealed class FakeWorldSaver : IWorldSaver
{
    public WorldSaveResult Result { get; set; } = WorldSaveResult.NotRunning;

    public List<string>? Journal { get; set; }

    public int CallCount { get; private set; }

    public Task<WorldSaveResult> SaveAsync(ServerId serverId, CancellationToken cancellationToken)
    {
        CallCount++;
        Journal?.Add("save");
        return Task.FromResult(Result);
    }
}
