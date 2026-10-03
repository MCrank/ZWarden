using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Mods;

/// <summary>
/// A Server's mod lists (#290, ADR 0047), one row per Server. It holds them twice:
/// <list type="bullet">
/// <item><b>Configured:</b> <c>WorkshopItems=</c> / <c>Mods=</c> as last observed in <c>servertest.ini</c>, which is
/// the desired state.</item>
/// <item><b>Booted with:</b> the same lists as of the last boot.</item>
/// </list>
/// "Pending" changes (installs or removals on restart) are the difference between the two. A boot
/// (<see cref="MarkBooted"/>) marks the snapshot pending, and the first config observation made at or after that boot
/// fills it: PZ read the file at launch, so the lists then are what it loaded. A cache that the next boot and
/// discovery rebuild, never the authority. <see cref="ITenantOwned"/> (ADR 0016). No concurrency token: last write
/// wins.
/// </summary>
public sealed class ServerModState : ITenantOwned
{
    /// <summary>The most entries stored per list.</summary>
    public const int MaxListEntries = 1000;

    /// <summary>The longest list entry stored.</summary>
    public const int MaxEntryLength = 256;

    /// <summary>EF / factory use.</summary>
    public ServerModState()
    {
    }

    /// <summary>The Server these lists belong to (also the key: one row per Server).</summary>
    public ServerId ServerId { get; init; }

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary><c>WorkshopItems=</c> as last observed, in file order.</summary>
    public IReadOnlyList<string> ConfiguredWorkshopIds { get; private set; } = [];

    /// <summary><c>Mods=</c> as last observed, in file (load) order.</summary>
    public IReadOnlyList<string> ConfiguredModIds { get; private set; } = [];

    /// <summary>When config was last observed, or <c>null</c> if never.</summary>
    public DateTimeOffset? ConfigObservedAt { get; private set; }

    /// <summary><c>WorkshopItems=</c> as the server last booted with it.</summary>
    public IReadOnlyList<string> BootedWorkshopIds { get; private set; } = [];

    /// <summary><c>Mods=</c> as the server last booted with it.</summary>
    public IReadOnlyList<string> BootedModIds { get; private set; } = [];

    /// <summary>When the server last booted, or <c>null</c> if no boot has been seen.</summary>
    public DateTimeOffset? BootedAt { get; private set; }

    /// <summary>When the booted-with lists were filled, or <c>null</c> if never.</summary>
    public DateTimeOffset? BootSnapshotAt { get; private set; }

    /// <summary>Whether a boot has been seen whose lists are not yet observed.</summary>
    public bool BootSnapshotPending { get; private set; }

    /// <summary>Whether the booted-with lists have been filled at least once.</summary>
    public bool HasBootSnapshot => BootSnapshotAt is not null;

    /// <summary>A fresh state for <paramref name="serverId"/>. The tenant is stamped on insert (ADR 0016).</summary>
    public static ServerModState For(ServerId serverId) => new() { ServerId = serverId };

    /// <summary>Records that the server booted at <paramref name="bootedAt"/>; the next config observation made at or
    /// after it fills the booted-with lists.</summary>
    public void MarkBooted(DateTimeOffset bootedAt)
    {
        BootedAt = bootedAt;
        BootSnapshotPending = true;
    }

    /// <summary>Records the configured lists observed at <paramref name="observedAt"/> and, if a boot is pending and
    /// this observation is not older than it, fills the booted-with lists from them.</summary>
    /// <exception cref="ArgumentException">A list or an entry exceeds its bound (the lists are Agent-reported).</exception>
    public void ObserveConfig(IReadOnlyList<string> workshopIds, IReadOnlyList<string> modIds, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(workshopIds);
        ArgumentNullException.ThrowIfNull(modIds);
        Guard(workshopIds, nameof(workshopIds));
        Guard(modIds, nameof(modIds));

        ConfiguredWorkshopIds = [.. workshopIds];
        ConfiguredModIds = [.. modIds];
        ConfigObservedAt = observedAt;

        if (BootSnapshotPending && BootedAt is { } bootedAt && observedAt >= bootedAt)
        {
            BootedWorkshopIds = [.. workshopIds];
            BootedModIds = [.. modIds];
            BootSnapshotAt = observedAt;
            BootSnapshotPending = false;
        }
    }

    private static void Guard(IReadOnlyList<string> list, string name)
    {
        if (list.Count > MaxListEntries || list.Any(e => e is null || e.Length > MaxEntryLength))
        {
            throw new ArgumentException(
                $"A mod list holds at most {MaxListEntries} entries of at most {MaxEntryLength} characters.", name);
        }
    }
}
