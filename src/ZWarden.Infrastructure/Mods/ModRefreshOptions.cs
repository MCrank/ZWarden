namespace ZWarden.Infrastructure.Mods;

/// <summary>Tuning for the background mod refresh (#290).</summary>
public sealed class ModRefreshOptions
{
    /// <summary>How long after a boot completes to discover again, and the gap between later re-checks. PZ downloads
    /// new Workshop items after Docker reports the start, so the first discovery can miss them (D2). Default 3
    /// minutes.</summary>
    public TimeSpan PostBootRediscoverDelay { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>How long after a boot to keep re-checking the disk while an item is still Update ready (#380): a large
    /// download can outlast one <see cref="PostBootRediscoverDelay"/>. Re-checks = this / the delay, at least one.
    /// Default 15 minutes (5 re-checks).</summary>
    public TimeSpan PostBootRediscoverWindow { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>When opening the Mods page shows an item Update ready, a disk discovery is queued only if the disk was
    /// last read longer ago than this (#380), so reopening the page doesn't flood the Agent. Default 1 minute.</summary>
    public TimeSpan UpdateReadyDiskMaxAge { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>How old an item's Steam details may get before a discovery refreshes them. Default 6 hours.</summary>
    public TimeSpan MetadataMaxAge { get; set; } = TimeSpan.FromHours(6);

    /// <summary>How often the mod-update check refreshes the Steam details of every Server with Workshop files on
    /// disk (#275 D4), so a running server that is never rediscovered still learns of mod updates. Default 1 hour.</summary>
    public TimeSpan UpdateCheckInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How old Steam details may be before the update check, or opening the Mods page, refreshes them. Kept
    /// below <see cref="UpdateCheckInterval"/> so a refresh made on one tick is due again on the next. Default
    /// 30 minutes.</summary>
    public TimeSpan UpdateCheckMaxAge { get; set; } = TimeSpan.FromMinutes(30);
}
