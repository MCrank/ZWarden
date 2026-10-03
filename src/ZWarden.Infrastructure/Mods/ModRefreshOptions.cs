namespace ZWarden.Infrastructure.Mods;

/// <summary>Tuning for the background mod refresh (#290).</summary>
public sealed class ModRefreshOptions
{
    /// <summary>How long after a boot completes to discover again. PZ downloads new Workshop items after Docker
    /// reports the start, so the first discovery can miss them (D2). Default 3 minutes.</summary>
    public TimeSpan PostBootRediscoverDelay { get; set; } = TimeSpan.FromMinutes(3);

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
