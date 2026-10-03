using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Mods;

/// <summary>
/// What the control plane knows about one Steam Workshop item on one Server (<c>wsi-</c>, #290, ADR 0047): its Steam
/// details (display data), the mod ids <b>guessed</b> from its Workshop description, and the mod ids <b>observed</b>
/// in <c>mod.info</c> on disk, which are the truth once the item has downloaded. It is a <b>cache and annotation, never
/// the authority</b>: the Server's <c>WorkshopItems=</c> / <c>Mods=</c> config is the desired state, and this record
/// can be rebuilt from config + disk + Steam. A row exists while its item is configured, on disk, or in the
/// booted-with snapshot (<see cref="ServerModState"/>). <see cref="ITenantOwned"/> (ADR 0016). No concurrency token:
/// it is rewritten wholesale from each observation, so last write wins.
/// </summary>
public sealed class ServerWorkshopItem : ITenantOwned
{
    /// <summary>The longest Workshop id accepted (Steam ids are numeric, up to 20 digits).</summary>
    public const int MaxWorkshopIdLength = 20;

    /// <summary>The longest title stored.</summary>
    public const int MaxTitleLength = 512;

    /// <summary>The longest preview URL stored.</summary>
    public const int MaxPreviewUrlLength = 2048;

    /// <summary>The most tags stored.</summary>
    public const int MaxTags = 32;

    /// <summary>The longest tag stored.</summary>
    public const int MaxTagLength = 64;

    /// <summary>The most mod ids stored per list (guessed or observed).</summary>
    public const int MaxModIds = 64;

    /// <summary>EF / factory use.</summary>
    public ServerWorkshopItem()
    {
    }

    /// <summary>The record identifier (<c>wsi-&lt;uuid&gt;</c>).</summary>
    public WorkshopItemId Id { get; init; } = WorkshopItemId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The Server this item belongs to.</summary>
    public ServerId ServerId { get; init; }

    /// <summary>The Steam Workshop item id (numeric).</summary>
    public string WorkshopId { get; init; } = string.Empty;

    /// <summary>The Steam title, or <c>null</c> before the first metadata refresh (untrusted; escaped at render).</summary>
    public string? Title { get; private set; }

    /// <summary>An absolute http(s) URL to the preview image, or <c>null</c>.</summary>
    public string? PreviewUrl { get; private set; }

    /// <summary>The item's content size in bytes, or <c>null</c>.</summary>
    public long? SizeBytes { get; private set; }

    /// <summary>When the item was last updated on the Workshop (UTC), or <c>null</c>.</summary>
    public DateTimeOffset? SteamUpdatedAt { get; private set; }

    /// <summary>The Workshop tags (e.g. <c>Build 42</c>).</summary>
    public IReadOnlyList<string> Tags { get; private set; } = [];

    /// <summary>When Steam details were last applied, or <c>null</c> if never.</summary>
    public DateTimeOffset? MetadataRefreshedAt { get; private set; }

    /// <summary>The mod ids parsed from the Workshop description: a best guess, validated as <see cref="PzModId"/>s.</summary>
    public IReadOnlyList<string> GuessedModIds { get; private set; } = [];

    /// <summary>The mod ids read from <c>mod.info</c> on disk at the last observation: the truth.</summary>
    public IReadOnlyList<string> ObservedModIds { get; private set; } = [];

    /// <summary>Whether the item's files were on disk at the last observation.</summary>
    public bool OnDisk { get; private set; }

    /// <summary>When disk was last observed for this item, or <c>null</c> if never.</summary>
    public DateTimeOffset? ObservedAt { get; private set; }

    /// <summary>Whether Steam details have been applied at least once.</summary>
    public bool HasMetadata => MetadataRefreshedAt is not null;

    /// <summary>Starts tracking <paramref name="workshopId"/> on <paramref name="serverId"/>. The tenant is stamped by
    /// the ownership interceptor on insert (ADR 0016).</summary>
    public static ServerWorkshopItem Track(ServerId serverId, string workshopId)
    {
        if (string.IsNullOrEmpty(workshopId) || workshopId.Length > MaxWorkshopIdLength || !workshopId.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("A Workshop id is 1–20 ASCII digits.", nameof(workshopId));
        }

        return new ServerWorkshopItem { Id = WorkshopItemId.New(), ServerId = serverId, WorkshopId = workshopId };
    }

    /// <summary>Records whether the item is on disk and, when it is, the mod ids its <c>mod.info</c> files declare.
    /// An item gone from disk has no observed ids.</summary>
    public void ObserveDisk(bool onDisk, IReadOnlyList<PzModId> modIds, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(modIds);
        OnDisk = onDisk;
        ObservedModIds = onDisk ? Bound(modIds) : [];
        ObservedAt = observedAt;
    }

    /// <summary>Applies Steam details from a successful lookup and the ids guessed from the description. A lookup
    /// that found nothing is not applied, so a Steam outage never wipes known details.</summary>
    public void ApplyMetadata(
        string? title,
        string? previewUrl,
        long? sizeBytes,
        DateTimeOffset? steamUpdatedAt,
        IReadOnlyList<string> tags,
        IReadOnlyList<PzModId> guessedModIds,
        DateTimeOffset refreshedAt)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(guessedModIds);

        Title = Truncate(title, MaxTitleLength);
        PreviewUrl = previewUrl is { Length: <= MaxPreviewUrlLength } ? previewUrl : null;
        SizeBytes = sizeBytes is >= 0 ? sizeBytes : null;
        SteamUpdatedAt = steamUpdatedAt;
        Tags = [.. tags.Where(t => !string.IsNullOrEmpty(t)).Take(MaxTags).Select(t => Truncate(t, MaxTagLength)!)];
        GuessedModIds = Bound(guessedModIds);
        MetadataRefreshedAt = refreshedAt;
    }

    private static string[] Bound(IReadOnlyList<PzModId> ids) =>
        [.. ids.Select(i => i.Value).Distinct(StringComparer.Ordinal).Take(MaxModIds)];

    private static string? Truncate(string? text, int max) =>
        string.IsNullOrEmpty(text) ? null : text.Length <= max ? text : text[..max];
}
