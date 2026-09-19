namespace ZWarden.Application.Workshop;

/// <summary>
/// The human-friendly metadata for a Steam Workshop item, enriched from Valve's <b>keyless</b>
/// <c>GetPublishedFileDetails</c> endpoint (#110). Purely display data — a richer view of a Workshop id F21 already
/// discovered or an operator pasted; it stores nothing and carries no Steam credential. Every field is <b>untrusted</b>
/// external JSON (trust-boundaries.md §8): bounded at parse, carried verbatim, escaped only at render.
/// <see cref="Found"/> is <c>false</c> when Steam returned no usable record for the id (deleted, hidden, unknown, or
/// the control plane is offline/air-gapped) — the caller still renders the bare numeric id, so enrichment degrades
/// gracefully and never blocks browse or install.
/// </summary>
/// <param name="WorkshopId">The Steam Workshop item id (numeric, as a string) this metadata is for.</param>
/// <param name="Found">Whether Steam returned a usable record for the id.</param>
/// <param name="Title">The item's title, or <c>null</c>.</param>
/// <param name="PreviewUrl">An absolute URL to the item's preview image, or <c>null</c>.</param>
/// <param name="SizeBytes">The item's content size in bytes, or <c>null</c>.</param>
/// <param name="UpdatedAt">When the item was last updated (UTC), or <c>null</c>.</param>
/// <param name="Description">The item's description, or <c>null</c> (bounded).</param>
public sealed record WorkshopItemMetadata(
    string WorkshopId,
    bool Found,
    string? Title = null,
    string? PreviewUrl = null,
    long? SizeBytes = null,
    DateTimeOffset? UpdatedAt = null,
    string? Description = null)
{
    /// <summary>A not-found placeholder for an id Steam did not resolve, so the caller renders the bare id.</summary>
    public static WorkshopItemMetadata NotFound(string workshopId) => new(workshopId, Found: false);
}
