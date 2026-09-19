using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Application.Workshop;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Domain.Workshop;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Workshop;

/// <summary>
/// The operator-facing Workshop-key surface (F110 PR-B; ADR 0044). Mutations require <c>Tenant.Manage</c>
/// tenant-wide (fail-closed) and audit the act — never the value (F6). The key is AEAD-encrypted through
/// <see cref="ISecretProtector"/> (ADR 0015) before it touches the database and is decrypted only when the
/// search client needs it. Settings are tenant-owned: the interceptor stamps the ambient tenant and the
/// repository reads through the tenant filter (ADR 0016).
/// </summary>
public sealed class WorkshopSettingsService : IWorkshopSettingsService
{
    // A Steam Web API key is 32 uppercase hex chars; accept a slightly wider alphanumeric band so a future
    // Valve format tweak does not lock operators out, while still rejecting whitespace/garbage/paste errors.
    private const int MinKeyLength = 16;
    private const int MaxKeyLength = 64;

    private readonly ZWardenDbContext _context;
    private readonly WorkshopIntegrationSettingsRepository _settings;
    private readonly IPermissionChecker _checker;
    private readonly IAuditWriter _audit;
    private readonly ISecretProtector _protector;

    public WorkshopSettingsService(
        ZWardenDbContext context,
        WorkshopIntegrationSettingsRepository settings,
        IPermissionChecker checker,
        IAuditWriter audit,
        ISecretProtector protector)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(checker);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(protector);
        _context = context;
        _settings = settings;
        _checker = checker;
        _audit = audit;
        _protector = protector;
    }

    /// <inheritdoc />
    public async Task SetApiKeyAsync(UserId actor, SecretString apiKey, CancellationToken cancellationToken = default)
    {
        await RequireTenantManageAsync(actor, cancellationToken).ConfigureAwait(false);

        string key = Validate(apiKey);
        string envelope = _protector.ProtectString(key);

        WorkshopIntegrationSettings? existing = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            WorkshopIntegrationSettings created = WorkshopIntegrationSettings.Create();
            created.SetProtectedApiKey(envelope);
            _settings.Add(created);
        }
        else
        {
            existing.SetProtectedApiKey(envelope);
        }

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await _audit.WriteAsync(
            new AuditEntry(WorkshopAuditActions.KeyConfigured, AuditOutcome.Succeeded, actor, null, "workshop search key set"),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ClearApiKeyAsync(UserId actor, CancellationToken cancellationToken = default)
    {
        await RequireTenantManageAsync(actor, cancellationToken).ConfigureAwait(false);

        WorkshopIntegrationSettings? existing = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (existing is { KeyConfigured: true })
        {
            existing.ClearApiKey();
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await _audit.WriteAsync(
            new AuditEntry(WorkshopAuditActions.KeyCleared, AuditOutcome.Succeeded, actor, null, "workshop search key cleared"),
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> IsSearchAvailableAsync(CancellationToken cancellationToken = default)
    {
        WorkshopIntegrationSettings? existing = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        return existing is { KeyConfigured: true };
    }

    /// <inheritdoc />
    public async Task<SecretString?> GetActiveApiKeyAsync(CancellationToken cancellationToken = default)
    {
        WorkshopIntegrationSettings? existing = await _settings.GetAsync(cancellationToken).ConfigureAwait(false);
        if (existing is not { KeyConfigured: true })
        {
            return null;
        }

        // Decrypt for one outbound call; the plaintext lives only inside the returned SecretString.
        return new SecretString(_protector.UnprotectString(existing.ProtectedApiKey));
    }

    private static string Validate(SecretString apiKey)
    {
        if (!apiKey.HasValue)
        {
            throw new ArgumentException("A Workshop search key is required.", nameof(apiKey));
        }

        string key = apiKey.Reveal().Trim();
        if (key.Length is < MinKeyLength or > MaxKeyLength || !key.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException(
                "That does not look like a Steam Web API key (expected 16-64 letters and digits).", nameof(apiKey));
        }

        return key;
    }

    private async Task RequireTenantManageAsync(UserId actor, CancellationToken cancellationToken)
    {
        IReadOnlySet<string> held = await _checker.GetTenantWidePermissionsAsync(actor, cancellationToken).ConfigureAwait(false);
        if (!held.Contains(Permissions.TenantManage.Name))
        {
            throw new AuthorizationDeniedException(
                $"{Permissions.TenantManage.Name} is required to manage Workshop integration settings.");
        }
    }
}
