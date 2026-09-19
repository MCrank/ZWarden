namespace ZWarden.Application.Workshop;

/// <summary>
/// A read-only client over Valve's <b>keyless</b> Steam Web API for Workshop metadata (#110). It reaches
/// <c>api.steampowered.com</c> from the control plane (never the Agent) and needs no API key — it uses only the two
/// endpoints Valve serves without one: <c>ISteamRemoteStorage/GetPublishedFileDetails</c> (lookup item details by id)
/// and <c>ISteamRemoteStorage/GetCollectionDetails</c> (expand a collection into member ids). It is <b>not</b> a
/// search — search all of Workshop needs a stored publisher key, which is the separate opt-in key setting (#110 PR-B).
/// <para>
/// Every method is resilient by contract: a network error, a timeout, an HTTP failure, or malformed/oversized JSON
/// yields an empty or not-found result, <b>never a throw</b> — enrichment is best-effort and must never break browse
/// or install. All returned strings are untrusted external data (trust-boundaries.md §8), bounded at parse and
/// escaped only at render.
/// </para>
/// </summary>
public interface IWorkshopMetadataClient
{
    /// <summary>Enriches <paramref name="workshopIds"/> via keyless <c>GetPublishedFileDetails</c>. Returns one entry
    /// per requested id (order not guaranteed); an id Steam did not resolve comes back as
    /// <see cref="WorkshopItemMetadata.NotFound"/>. An empty or over-large request returns an empty list.</summary>
    Task<IReadOnlyList<WorkshopItemMetadata>> GetItemsAsync(
        IReadOnlyList<string> workshopIds, CancellationToken cancellationToken = default);

    /// <summary>Expands a Workshop <paramref name="collectionId"/> into its member item ids via keyless
    /// <c>GetCollectionDetails</c>, in the collection's sort order. Returns an empty list when the id is not a
    /// resolvable collection or the call fails.</summary>
    Task<IReadOnlyList<string>> GetCollectionItemIdsAsync(
        string collectionId, CancellationToken cancellationToken = default);
}
