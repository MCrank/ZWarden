using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Application.Workshop;

/// <summary>
/// Manages a tenant's optional Workshop <b>search</b> key (F110 PR-B; ADR 0044). Keyless enrichment needs no
/// key and is unaffected by this surface; configuring a key is what lights up <c>QueryFiles</c> search.
/// <para>
/// Mutating the key is a high-privilege act: <see cref="SetApiKeyAsync"/> and <see cref="ClearApiKeyAsync"/>
/// require the actor to hold <c>Tenant.Manage</c> tenant-wide (fail-closed) and audit the outcome (F6) — the
/// key value is <b>never</b> written to an audit record, only the act. The key is encrypted at rest via
/// <c>ISecretProtector</c> (ADR 0015); the plaintext is revealed only for the moment of encryption and, later,
/// for a single <c>QueryFiles</c> call. The write-only UI never reads it back — it reads only
/// <see cref="IsSearchAvailableAsync"/>.
/// </para>
/// </summary>
public interface IWorkshopSettingsService
{
    /// <summary>
    /// Encrypts and stores <paramref name="apiKey"/> as the ambient tenant's search key (upserting the single
    /// per-tenant row), then audits. Requires <c>Tenant.Manage</c>; throws
    /// <see cref="ZWarden.Application.Authorization.AuthorizationDeniedException"/> otherwise, writing nothing.
    /// Rejects a malformed key (empty/whitespace, or characters outside a plausible key alphabet).
    /// </summary>
    Task SetApiKeyAsync(UserId actor, SecretString apiKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the ambient tenant to keyless mode by clearing any stored key, then audits. Idempotent (clearing
    /// when none is configured is a no-op write). Requires <c>Tenant.Manage</c>; throws otherwise.
    /// </summary>
    Task ClearApiKeyAsync(UserId actor, CancellationToken cancellationToken = default);

    /// <summary>
    /// The capability check the search path and the UI read: <c>true</c> when the ambient tenant has a key
    /// configured. Reads only the non-secret flag — it never decrypts, and is not actor-gated (any viewer of
    /// the mod browser may learn whether search is available; the key itself is never exposed).
    /// </summary>
    Task<bool> IsSearchAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// <b>Control-plane internal.</b> Decrypts and returns the ambient tenant's key for a single outbound
    /// <c>QueryFiles</c> call, or <c>null</c> in keyless mode. The result is a <see cref="SecretString"/> so it
    /// cannot be logged or serialized by accident; it is never returned to the UI and never cached in plaintext.
    /// </summary>
    Task<SecretString?> GetActiveApiKeyAsync(CancellationToken cancellationToken = default);
}
