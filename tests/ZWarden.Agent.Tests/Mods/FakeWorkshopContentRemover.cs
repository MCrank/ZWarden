using ZWarden.Agent.Mods;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Tests.Mods;

/// <summary>A scripted <see cref="IWorkshopContentRemover"/> for the command-processor tests (#293).</summary>
internal sealed class FakeWorkshopContentRemover : IWorkshopContentRemover
{
    public WorkshopContentRemoval Removal { get; set; } = new(true, null, new([]));

    public ServerId? LastServerId { get; private set; }

    public IReadOnlyList<string>? LastWorkshopIds { get; private set; }

    public int CallCount { get; private set; }

    public Task<WorkshopContentRemoval> DeleteAsync(
        ServerId serverId, IReadOnlyList<string> workshopIds, CancellationToken cancellationToken)
    {
        LastServerId = serverId;
        LastWorkshopIds = workshopIds;
        CallCount++;
        return Task.FromResult(Removal);
    }
}
