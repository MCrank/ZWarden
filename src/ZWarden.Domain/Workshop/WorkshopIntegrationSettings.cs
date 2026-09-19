using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Workshop;

/// <summary>
/// A tenant's <b>Workshop integration settings</b> (<c>wis-</c>, F110) — one row per tenant holding the
/// optional, operator-supplied Steam Web API key that unlocks Workshop <i>search</i> (ADR 0044). Keyless
/// enrichment (names, previews, paste-an-id) needs no secret and is unaffected by this row; a stored key adds
/// only the free-text search capability on top, so the key's <b>presence</b> is a capability flag.
/// <para>
/// The key is held here <b>only as the opaque authenticated-encryption envelope</b> produced by
/// <c>ISecretProtector</c> (ADR 0015) — never plaintext, and the domain does no crypto (the application layer
/// encrypts before calling <see cref="SetProtectedApiKey"/> and decrypts what <see cref="ProtectedApiKey"/>
/// returns). <see cref="KeyConfigured"/> is a derived, non-secret flag the UI and the search capability check
/// read without ever touching the envelope, so there is no second source of truth to drift.
/// </para>
/// <para>
/// It is <see cref="ITenantOwned"/> (stamped and filtered by the ambient tenant, ADR 0016) and
/// <see cref="IVersioned"/>. Per-tenant from day one: install-wide in a single-tenant self-host, per-customer
/// in SaaS with no schema change.
/// </para>
/// </summary>
public sealed class WorkshopIntegrationSettings : IVersioned, ITenantOwned
{
    /// <summary>EF / factory use.</summary>
    public WorkshopIntegrationSettings()
    {
    }

    /// <summary>The settings identifier (<c>wis-&lt;uuid&gt;</c>).</summary>
    public WorkshopIntegrationSettingsId Id { get; init; } = WorkshopIntegrationSettingsId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>
    /// The Steam Web API key stored as an <c>ISecretProtector</c> AEAD envelope string, or the empty string
    /// when no key is configured (keyless mode). Never plaintext; only the application layer decrypts it, and
    /// only at the moment of a <c>QueryFiles</c> call.
    /// </summary>
    public string ProtectedApiKey { get; private set; } = string.Empty;

    /// <inheritdoc />
    public Guid Version { get; set; }

    /// <summary>
    /// Whether a search key is configured — the non-secret capability flag. Derived from the presence of
    /// <see cref="ProtectedApiKey"/> so it can never disagree with what is actually stored. Not a mapped
    /// column (get-only).
    /// </summary>
    public bool KeyConfigured => ProtectedApiKey.Length > 0;

    /// <summary>
    /// Creates a tenant's settings row in the keyless (no key configured) state. The <see cref="TenantId"/> is
    /// left unset so the ownership interceptor stamps the ambient tenant on insert (ADR 0016).
    /// </summary>
    public static WorkshopIntegrationSettings Create() => new()
    {
        Id = WorkshopIntegrationSettingsId.New(),
        ProtectedApiKey = string.Empty,
    };

    /// <summary>
    /// Stores <paramref name="protectedApiKey"/> — the already-encrypted envelope (never plaintext), which is
    /// why an empty or whitespace value is rejected: an empty key is a <see cref="ClearApiKey"/>, not a set.
    /// </summary>
    public void SetProtectedApiKey(string protectedApiKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectedApiKey);
        ProtectedApiKey = protectedApiKey;
    }

    /// <summary>Returns to keyless mode: clears the stored envelope so <see cref="KeyConfigured"/> is false.</summary>
    public void ClearApiKey() => ProtectedApiKey = string.Empty;
}
