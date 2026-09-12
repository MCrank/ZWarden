using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Persistence;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Authorization;

/// <summary>
/// F5 S5 (PR 1): the deny-only safety-rule seam (PRD 12A; ADR 0018). A rule may veto an otherwise-allowed
/// decision but can never grant one. Offline tier.
/// </summary>
public class SafetyRuleTests
{
    private static readonly TenantId TenantA = TenantId.New();

    private sealed class DenyPermissionRule : IAuthorizationSafetyRule
    {
        private readonly string _permissionName;

        public DenyPermissionRule(string permissionName) => _permissionName = permissionName;

        public Task<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(request.Permission.Name == _permissionName
                ? AuthorizationDecision.Deny($"Blocked {_permissionName} by policy.")
                : AuthorizationDecision.Allow(request.Permission, request.Server));
    }

    private sealed class AllowEverythingRule : IAuthorizationSafetyRule
    {
        public Task<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(AuthorizationDecision.Allow(request.Permission, request.Server));
    }

    [Test]
    public async Task A_deny_only_rule_vetoes_an_otherwise_allowed_decision()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            await SeedTenantWideGrantAsync(options, user, Permissions.RoleManage);

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));

            // Without the rule the grant is allowed.
            PermissionChecker unguarded = new(db, new TestTenantContext(TenantA));
            await Assert.That((await unguarded.EvaluateAsync(user, Permissions.RoleManage)).IsAllowed).IsTrue();

            // With a rule that vetoes Role.Manage, the same grant is denied.
            PermissionChecker guarded = new(db, new TestTenantContext(TenantA),
                [new DenyPermissionRule(Permissions.RoleManage.Name)]);
            await Assert.That((await guarded.EvaluateAsync(user, Permissions.RoleManage)).IsAllowed).IsFalse();
        });
    }

    [Test]
    public async Task A_rule_cannot_turn_a_deny_into_an_allow()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New(); // no grants at all

            await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
            PermissionChecker checker = new(db, new TestTenantContext(TenantA), [new AllowEverythingRule()]);

            // The base decision is a deny (no grant); a rule that "allows" cannot flip it.
            await Assert.That((await checker.EvaluateAsync(user, Permissions.RoleManage)).IsAllowed).IsFalse();
        });
    }

    private static async Task SeedTenantWideGrantAsync(DbContextOptions options, UserId user, PermissionDefinition permission)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(TenantA));
        Role role = Role.CreateCustom(TenantA, $"role-{Guid.NewGuid():N}");
        role.Grant(permission);
        db.Set<Role>().Add(role);
        db.Set<RoleAssignment>().Add(RoleAssignment.TenantWide(TenantA, user, role.Id));
        await db.SaveChangesAsync();
    }

    private static async Task WithSqlite(Func<DbContextOptions, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using (ZWardenDbContext db = new(options, new TestTenantContext(TenantA)))
            {
                await db.Database.EnsureCreatedAsync();
            }

            await body(options);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }
}
