namespace ZWarden.Application.Workshop;

/// <summary>
/// Searches the Steam Workshop by free text (F110 PR-B; ADR 0044). Unlike keyless lookup-by-id, search hits
/// <c>IPublishedFileService/QueryFiles</c>, which requires the tenant's stored Steam Web API key — so this is a
/// <b>key-gated capability</b>. With no key configured the service returns
/// <see cref="WorkshopSearchResults.Unavailable"/> and makes no outbound call.
/// </summary>
public interface IWorkshopSearchService
{
    /// <summary>
    /// Returns Workshop items matching <paramref name="query"/>, or <see cref="WorkshopSearchResults.Unavailable"/>
    /// when the tenant has no key (or the stored key is rejected). Never throws: a transient failure degrades to
    /// an empty result. Results are untrusted external data (trust-boundaries §8) — bounded here, escaped at render.
    /// </summary>
    Task<WorkshopSearchResults> SearchAsync(string query, CancellationToken cancellationToken = default);
}

/// <summary>One Workshop item in a search result. All fields but the id are optional (Steam may omit any) and
/// carried verbatim from untrusted JSON — escaped only at render (trust-boundaries §8).</summary>
public sealed record WorkshopSearchResult(
    string WorkshopId,
    string? Title = null,
    string? PreviewUrl = null,
    long? Subscriptions = null,
    DateTimeOffset? UpdatedAt = null);

/// <summary>
/// The outcome of a search. <see cref="SearchAvailable"/> is <c>false</c> only when search is not usable at all
/// (no key configured, or the key was rejected) — distinct from an available search that simply matched nothing.
/// </summary>
public sealed record WorkshopSearchResults(bool SearchAvailable, IReadOnlyList<WorkshopSearchResult> Items)
{
    /// <summary>Search is not usable for this tenant (keyless, or the stored key was rejected).</summary>
    public static WorkshopSearchResults Unavailable { get; } = new(false, []);

    /// <summary>Search is available but matched nothing (or a transient failure yielded no items).</summary>
    public static WorkshopSearchResults None { get; } = new(true, []);

    /// <summary>Search is available and produced <paramref name="items"/>.</summary>
    public static WorkshopSearchResults From(IReadOnlyList<WorkshopSearchResult> items) => new(true, items);
}
