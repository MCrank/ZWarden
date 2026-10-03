namespace ZWarden.Infrastructure.Mods;

/// <summary>Tuning for the background mod refresh (#290).</summary>
public sealed class ModRefreshOptions
{
    /// <summary>How long after a boot completes to discover again. PZ downloads new Workshop items after Docker
    /// reports the start, so the first discovery can miss them (D2). Default 3 minutes.</summary>
    public TimeSpan PostBootRediscoverDelay { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>How old an item's Steam details may get before a discovery refreshes them. Default 6 hours.</summary>
    public TimeSpan MetadataMaxAge { get; set; } = TimeSpan.FromHours(6);
}
