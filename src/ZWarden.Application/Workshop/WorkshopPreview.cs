namespace ZWarden.Application.Workshop;

/// <summary>Why a <see cref="WorkshopPreview"/> did or did not resolve — a typed outcome the UI maps to a message,
/// so resolution never throws.</summary>
public enum WorkshopPreviewStatus
{
    /// <summary>The input carried an id and was resolved; <see cref="WorkshopPreview.Items"/> holds the metadata
    /// (a single item, or a collection's members). Individual items may still be <see cref="WorkshopItemMetadata.Found"/>
    /// <c>false</c> — Steam had no record, or the control plane is offline — which the UI renders as a bare id.</summary>
    Resolved,

    /// <summary>The input did not contain a Workshop id (empty, or an unrecognised string/URL).</summary>
    Unresolvable,

    /// <summary>The caller does not hold <c>Mod.View</c> on the server (fail-closed, ADR 0018).</summary>
    NotAuthorized,

    /// <summary>No such server in the caller's tenant.</summary>
    ServerNotFound,
}

/// <summary>
/// The outcome of resolving an operator-pasted Workshop reference for preview (#110 PR-C). On
/// <see cref="WorkshopPreviewStatus.Resolved"/> it carries one or more <see cref="WorkshopItemMetadata"/> (a single
/// item, or every member of a collection, in the collection's order); otherwise the list is empty and
/// <see cref="Status"/> says why. All fields are untrusted external data (trust-boundaries.md §8).
/// </summary>
/// <param name="Status">The typed resolution outcome.</param>
/// <param name="IsCollection">Whether the reference resolved to a collection (its members are in <see cref="Items"/>).</param>
/// <param name="Items">The resolved item metadata, in order; empty unless <see cref="Status"/> is Resolved.</param>
public sealed record WorkshopPreview(
    WorkshopPreviewStatus Status,
    bool IsCollection,
    IReadOnlyList<WorkshopItemMetadata> Items)
{
    /// <summary>The caller is not permitted to preview Workshop content for this server.</summary>
    public static WorkshopPreview NotAuthorized { get; } = new(WorkshopPreviewStatus.NotAuthorized, false, []);

    /// <summary>The server is unknown to the caller's tenant.</summary>
    public static WorkshopPreview ServerNotFound { get; } = new(WorkshopPreviewStatus.ServerNotFound, false, []);

    /// <summary>The input carried no recognisable Workshop id.</summary>
    public static WorkshopPreview Unresolvable { get; } = new(WorkshopPreviewStatus.Unresolvable, false, []);

    /// <summary>A resolved single item (or a not-found placeholder for the id).</summary>
    public static WorkshopPreview OfItem(WorkshopItemMetadata item) =>
        new(WorkshopPreviewStatus.Resolved, false, [item]);

    /// <summary>A resolved collection and its member items, in order.</summary>
    public static WorkshopPreview OfCollection(IReadOnlyList<WorkshopItemMetadata> items) =>
        new(WorkshopPreviewStatus.Resolved, true, items);
}
