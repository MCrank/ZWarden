using ZWarden.Application.Authorization;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Servers;

namespace ZWarden.Infrastructure.Workshop;

/// <summary>
/// The authorized, server-scoped preview service behind the Mod Browser (#110 PR-C). Fail-closed (ADR 0018): it
/// resolves the Server through the tenant filter and requires the caller to hold <c>Mod.View</c> on it before it
/// composes the keyless <see cref="IWorkshopMetadataClient"/> to resolve a pasted id or collection URL — so an
/// unauthorized viewer can never drive the control plane's Steam egress. A pasted reference is tried as a collection
/// first (expanding to its members) and, failing that, as a single item. It never throws: an id Steam cannot resolve
/// comes back as a not-found item the UI renders as a bare id, and an input with no id is <c>Unresolvable</c>.
/// </summary>
public sealed class WorkshopMetadataService : IWorkshopMetadataService
{
    private readonly ServerRepository _servers;
    private readonly IPermissionChecker _permissions;
    private readonly IWorkshopMetadataClient _client;

    public WorkshopMetadataService(
        ServerRepository servers, IPermissionChecker permissions, IWorkshopMetadataClient client)
    {
        ArgumentNullException.ThrowIfNull(servers);
        ArgumentNullException.ThrowIfNull(permissions);
        ArgumentNullException.ThrowIfNull(client);
        _servers = servers;
        _permissions = permissions;
        _client = client;
    }

    /// <inheritdoc />
    public async Task<WorkshopPreview> ResolveAsync(
        UserId actor, ServerId server, string input, CancellationToken cancellationToken = default)
    {
        // Fail-closed: an unknown/foreign server is ServerNotFound; a known one without Mod.View is NotAuthorized.
        // Authorize before touching Steam so an unauthorized caller cannot drive the control plane's egress.
        Server? resolved = await _servers.FindByIdAsync(server, cancellationToken).ConfigureAwait(false);
        if (resolved is null)
        {
            return WorkshopPreview.ServerNotFound;
        }

        AuthorizationDecision decision = await _permissions
            .EvaluateAsync(actor, Permissions.ModView, server: server, cancellationToken).ConfigureAwait(false);
        if (!decision.IsAllowed)
        {
            return WorkshopPreview.NotAuthorized;
        }

        if (!WorkshopReference.TryParseId(input, out string id))
        {
            return WorkshopPreview.Unresolvable;
        }

        // Try the reference as a collection first — GetCollectionItemIds returns empty for a non-collection id, so a
        // plain item falls through to the single-item lookup.
        IReadOnlyList<string> memberIds = await _client
            .GetCollectionItemIdsAsync(id, cancellationToken).ConfigureAwait(false);
        if (memberIds.Count > 0)
        {
            IReadOnlyList<WorkshopItemMetadata> members = await _client
                .GetItemsAsync(memberIds, cancellationToken).ConfigureAwait(false);
            return WorkshopPreview.OfCollection(members);
        }

        IReadOnlyList<WorkshopItemMetadata> items = await _client
            .GetItemsAsync([id], cancellationToken).ConfigureAwait(false);
        return WorkshopPreview.OfItem(items.Count > 0 ? items[0] : WorkshopItemMetadata.NotFound(id));
    }
}
