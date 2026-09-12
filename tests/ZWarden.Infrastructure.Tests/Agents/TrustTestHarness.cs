using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ZWarden.Application.Agents;
using ZWarden.Application.Audit;
using ZWarden.Application.Authorization;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Agents;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Security;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Agents;

/// <summary>Shared fixtures for the F9 S4 trust-service tests: a real SQLite database, a fixed clock, a
/// capturing audit writer, and a permission-checker stub, plus factories that wire the services the way DI
/// will (ADR 0007).</summary>
internal static class TrustTestHarness
{
    public static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    public static readonly TenantId Tenant = TenantId.New();
    public static readonly UserId Manager = UserId.New();
    public static readonly string ManagePermission = Permissions.TenantEnrollmentManage.Name;
    public static readonly CredentialHasher Hasher = new();

    public static ZWardenDbContext Context(DbContextOptions options) => new(options, new TestTenantContext(Tenant));

    public static EnrollmentService Enrollment(
        ZWardenDbContext ctx,
        CapturingAuditWriter audit,
        string[] held,
        DateTimeOffset? now = null)
        => new(
            ctx,
            new EnrollmentRepository(ctx),
            new StubPermissionChecker(held),
            audit,
            Hasher,
            new StubClock(now ?? Now),
            Options.Create(new EnrollmentOptions()));

    public static AgentTrustService Trust(
        ZWardenDbContext ctx,
        CapturingAuditWriter audit,
        string[] held)
        => new(ctx, new AgentRepository(ctx), new StubPermissionChecker(held), audit, Hasher, new StubClock(Now));

    public static AgentEnrollmentExchange Exchange(
        ZWardenDbContext ctx,
        CapturingAuditWriter audit,
        DateTimeOffset? now = null)
        => new(ctx, new EnrollmentRepository(ctx), audit, Hasher, new StubClock(now ?? Now));

    public static AgentCredentialVerifier Verifier(ZWardenDbContext ctx)
        => new(new AgentRepository(ctx), Hasher);

    public static async Task WithSqlite(Func<DbContextOptions, Task> body)
    {
        string file = Path.Combine(Path.GetTempPath(), $"zw-{Guid.NewGuid():N}.db");
        DbContextOptions options = new DbContextOptionsBuilder<ZWardenDbContext>()
            .UseZWardenProvider(ZWardenDbProvider.Sqlite, $"Data Source={file};Pooling=False")
            .Options;
        try
        {
            await using (ZWardenDbContext db = Context(options))
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

internal sealed class StubClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal sealed class CapturingAuditWriter : IAuditWriter
{
    public List<AuditEntry> Entries { get; } = [];

    public IReadOnlyList<string> Actions => Entries.Select(e => e.Action).ToList();

    public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

internal sealed class StubPermissionChecker(params string[] tenantWide) : IPermissionChecker
{
    private readonly IReadOnlySet<string> _held = tenantWide.ToHashSet(StringComparer.Ordinal);

    public Task<AuthorizationDecision> EvaluateAsync(
        UserId user,
        PermissionDefinition permission,
        ServerId? server = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The trust services check tenant-wide permissions only.");

    public Task<IReadOnlySet<string>> GetTenantWidePermissionsAsync(
        UserId user,
        CancellationToken cancellationToken = default)
        => Task.FromResult(_held);
}
