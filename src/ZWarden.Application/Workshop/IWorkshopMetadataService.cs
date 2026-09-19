using ZWarden.Domain.Ids;

namespace ZWarden.Application.Workshop;

/// <summary>
/// The authorized, server-scoped preview surface behind the Mod Browser (#110 PR-C). It resolves an operator-pasted
/// Workshop item id or collection URL to display metadata by composing the keyless
/// <see cref="IWorkshopMetadataClient"/> — no API key, control-plane egress only. Unlike the client, which is a raw
/// enrichment seam, this service is <b>fail-closed</b> (ADR 0018): it resolves the Server through the tenant filter
/// and requires the caller to hold <c>Mod.View</c> on it before making any outbound call, so an unauthorized viewer
/// cannot drive the control plane's Steam egress. It never throws — every outcome is a typed
/// <see cref="WorkshopPreview"/>. All returned strings are untrusted external data (trust-boundaries.md §8),
/// bounded at parse and escaped only at render.
/// </summary>
public interface IWorkshopMetadataService
{
    /// <summary>
    /// Resolves <paramref name="input"/> (a bare Workshop id, or a Steam URL carrying one) to preview metadata,
    /// authorizing <c>Mod.View</c> on <paramref name="server"/> first. A collection reference expands to its member
    /// items; a single reference resolves to one item (whose <see cref="WorkshopItemMetadata.Found"/> is
    /// <c>false</c> when Steam has no record or the control plane is offline). Returns a typed non-resolved outcome
    /// when the caller is unauthorized, the server is unknown, or the input carries no id.
    /// </summary>
    Task<WorkshopPreview> ResolveAsync(
        UserId actor, ServerId server, string input, CancellationToken cancellationToken = default);
}
