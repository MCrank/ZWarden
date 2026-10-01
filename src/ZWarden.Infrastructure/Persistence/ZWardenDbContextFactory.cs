using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Tenancy;
using ZWarden.Domain.Security;

namespace ZWarden.Infrastructure.Persistence;

/// <summary>
/// The scoped <see cref="IDbContextFactory{TContext}"/> for <see cref="ZWardenDbContext"/> (#297, ADR 0046 Q7).
/// Every context it creates is bound to <b>this scope's</b> tenant and, when the security foundation is present,
/// encrypts sensitive Identity values at rest (ADR 0015). The scoped <see cref="ZWardenDbContext"/> registration is
/// built from it too, so there is one construction path.
/// </summary>
/// <remarks>
/// Registered scoped, not singleton: a singleton factory would capture no tenant. Interactive components don't
/// call it directly; they run each action in its own scope (<c>ActionScopeRunner</c>), so the action's services
/// all share that scope's single context.
/// </remarks>
public sealed class ZWardenDbContextFactory : IDbContextFactory<ZWardenDbContext>
{
    private readonly DbContextOptions<ZWardenDbContext> _options;
    private readonly ITenantContext _tenantContext;
    private readonly ISecretProtector? _protector;

    public ZWardenDbContextFactory(
        DbContextOptions<ZWardenDbContext> options,
        ITenantContext tenantContext,
        ISecretProtector? protector)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tenantContext);
        _options = options;
        _tenantContext = tenantContext;
        _protector = protector;
    }

    /// <inheritdoc />
    public ZWardenDbContext CreateDbContext() => _protector is null
        ? new ZWardenDbContext(_options, _tenantContext)
        : new ZWardenDbContext(_options, _tenantContext, _protector);
}
