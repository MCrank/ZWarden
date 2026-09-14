using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Configuration;
using ZWarden.Application.Operations;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Configuration;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Configuration;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Configuration;

/// <summary>
/// F20b PR-3: the configuration-apply enqueue service, fail-closed (ADR 0018). It authorizes the server-scoped
/// <c>ServerConfigurationEdit</c> permission against the specific Server, resolves the Server through the tenant
/// filter (foreign/unknown ⇒ ServerNotFound), validates the edits, captures the last revision's hash as the
/// drift baseline (ADR 0011), audits, and enqueues a <b>mutating</b>, server-scoped Operation whose payload
/// carries the edits + baseline; a busy server (per-server lock, ADR 0022) surfaces as ServerBusy. Proven against
/// a real SQLite database, a real <see cref="PermissionChecker"/>, and seeded assignments.
/// </summary>
public class ServerConfigurationEditorTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 10, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<ConfigApplyEdit> Edits =
        [new ConfigApplyEdit("Zombies", ConfigEditKind.Number, "1")];

    [Test]
    public async Task Apply_enqueues_a_mutating_config_operation_carrying_the_edits_and_baseline()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            await SeedRevisionAsync(options, serverId, PzConfigFile.SandboxVars, "base-hash-1");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, audit);

            ServerConfigurationResult result = await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, Edits);

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(result.Operation).IsNotNull();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.ConfigApply);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsTrue();
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId);
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);

            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.File).IsEqualTo(PzConfigFile.SandboxVars);
            await Assert.That(payload.BaselineHash).IsEqualTo("base-hash-1");
            await Assert.That(payload.Edits.Count).IsEqualTo(1);
            await Assert.That(payload.Edits[0].Path).IsEqualTo("Zombies");
            await Assert.That(audit.Actions).Contains(ConfigurationAuditActions.Applied);
        });
    }

    [Test]
    public async Task Apply_uses_a_null_baseline_when_no_revision_has_been_recorded()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, Edits);

            await Assert.That(result.Succeeded).IsTrue();
            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.BaselineHash).IsNull();
        });
    }

    [Test]
    public async Task Apply_uses_the_baseline_for_the_targeted_file_only()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);
            // A revision for a *different* file must not become this file's baseline.
            await SeedRevisionAsync(options, serverId, PzConfigFile.Ini, "ini-hash");

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, Edits);

            ConfigApplyPayload payload = ConfigApplyPayload.FromJson(coordinator.LastRequest!.CommandPayload!);
            await Assert.That(payload.BaselineHash).IsNull();
        });
    }

    [Test]
    public async Task Apply_denies_without_the_server_scoped_config_edit_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            // No ServerConfigurationEdit assignment.

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, Edits);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Apply_reports_server_not_found_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId unknown = ServerId.New();
            await SeedAssignmentAsync(options, user, unknown, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerConfigurationEditor sut = Editor(db, new RecordingCoordinator(), new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(user, unknown, PzConfigFile.SandboxVars, Edits);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.ServerNotFound);
        });
    }

    [Test]
    public async Task Apply_reports_server_busy_when_the_per_server_lock_refuses()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ServerConfigurationEditor sut = Editor(db, new RecordingCoordinator { ThrowBusy = true }, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, Edits);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.ServerBusy);
        });
    }

    [Test]
    public async Task Apply_denies_an_empty_edit_set()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(user, serverId, PzConfigFile.SandboxVars, []);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Apply_denies_an_edit_with_an_empty_path()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId serverId = await SeedServerAsync(options, AgentId.New());
            await SeedAssignmentAsync(options, user, serverId, Permissions.ServerConfigurationEdit);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ServerConfigurationEditor sut = Editor(db, coordinator, new CapturingAuditWriter());

            ServerConfigurationResult result = await sut.ApplyAsync(
                user, serverId, PzConfigFile.SandboxVars, [new ConfigApplyEdit("  ", ConfigEditKind.Number, "1")]);

            await Assert.That(result.Failure).IsEqualTo(ServerConfigurationFailure.InvalidInput);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    private static ServerConfigurationEditor Editor(
        ZWardenDbContext db, RecordingCoordinator coordinator, CapturingAuditWriter audit)
        => new(
            new ServerRepository(db),
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            coordinator,
            new ConfigurationRevisionRepository(db),
            audit);

    private sealed class RecordingCoordinator : IOperationCoordinator
    {
        public bool ThrowBusy { get; init; }

        public EnqueueOperationRequest? LastRequest { get; private set; }

        public Task<Operation> EnqueueAsync(
            EnqueueOperationRequest request, UserId? actor = null, CancellationToken cancellationToken = default)
        {
            if (ThrowBusy)
            {
                throw new ServerBusyException(request.ServerId!.Value);
            }

            LastRequest = request;
            return Task.FromResult(Operation.Enqueue(
                request.AgentId, request.Kind, request.IsMutating, request.IdempotencyKey, Now,
                request.ServerId, request.CommandPayload));
        }

        public Task<Operation> RequestCancellationAsync(
            OperationId operationId, UserId? actor = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task SeedRevisionAsync(DbContextOptions options, ServerId server, PzConfigFile file, string hash)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        new ConfigurationRevisionRepository(db).Add(
            ConfigurationRevision.Record(server, file, "[[\"Zombies\",\"n:4:i\"]]", hash, Now));
        await db.SaveChangesAsync();
    }

    private static async Task SeedAssignmentAsync(
        DbContextOptions options, UserId user, ServerId server, PermissionDefinition permission)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Role role = Role.CreateCustom(Tenant, $"role-{Guid.NewGuid():N}");
        role.Grant(permission);
        db.Set<Role>().Add(role);
        db.Set<RoleAssignment>().Add(RoleAssignment.ForServer(Tenant, user, role.Id, server));
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
            await using (ZWardenDbContext db = new(options, new TestTenantContext(Tenant)))
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
