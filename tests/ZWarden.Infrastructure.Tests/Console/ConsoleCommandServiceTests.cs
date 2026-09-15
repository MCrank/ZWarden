using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Console;
using ZWarden.Application.Operations;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Authorization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Servers;
using ZWarden.Infrastructure.Authorization;
using ZWarden.Infrastructure.Console;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Servers;
using ZWarden.Infrastructure.Tests.Agents;
using ZWarden.TestSupport;

namespace ZWarden.Infrastructure.Tests.Console;

/// <summary>
/// F28: the remote-console service, fail-closed (ADR 0018). A submission authorizes the <b>elevated</b>
/// server-scoped <c>Console.Execute</c> permission against the specific Server, resolves the Server through the
/// tenant filter (foreign/unknown ⇒ ServerNotFound), runs the F28 input-safety + command policy (ADR 0032), then
/// enqueues a <b>non-mutating</b> ExecuteConsoleCommand on the Server's Agent carrying the command line — and
/// audits it. A policy denial is audited and never enqueued; unsafe input is a plain rejection. Proven against a
/// real SQLite database, a real <see cref="PermissionChecker"/>, and seeded assignments.
/// </summary>
public class ConsoleCommandServiceTests
{
    private static readonly TenantId Tenant = TenantId.New();
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task Execute_enqueues_a_non_mutating_console_operation_carrying_the_line_and_audits_it()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ConsoleExecute);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ConsoleCommandService sut = Service(db, coordinator, audit);

            ConsoleExecutionResult result = await sut.ExecuteAsync(user, serverId, "servermsg \"hello\"");

            await Assert.That(result.Succeeded).IsTrue();
            await Assert.That(coordinator.LastRequest!.Kind).IsEqualTo(OperationKind.ExecuteConsoleCommand);
            await Assert.That(coordinator.LastRequest!.IsMutating).IsFalse(); // never takes the per-server lock
            await Assert.That(coordinator.LastRequest!.ServerId).IsEqualTo(serverId);
            await Assert.That(coordinator.LastRequest!.AgentId).IsEqualTo(agent);
            await Assert.That(ConsoleCommandPayload.FromJson(coordinator.LastRequest!.CommandPayload!).Input)
                .IsEqualTo("servermsg \"hello\"");
            await Assert.That(audit.Actions).Contains(ConsoleAuditActions.CommandExecuted);
        });
    }

    [Test]
    public async Task Execute_denies_without_the_elevated_console_execute_permission()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            // Console.View is not enough — execution needs the elevated Console.Execute.
            await SeedAssignmentAsync(options, user, serverId, Permissions.ConsoleView);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ConsoleCommandService sut = Service(db, coordinator, new CapturingAuditWriter());

            ConsoleExecutionResult result = await sut.ExecuteAsync(user, serverId, "players");

            await Assert.That(result.Succeeded).IsFalse();
            await Assert.That(result.Failure).IsEqualTo(ConsoleCommandFailure.NotAuthorized);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    [Test]
    public async Task Execute_reports_server_not_found_for_an_unknown_server()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            ServerId unknown = ServerId.New();
            await SeedAssignmentAsync(options, user, unknown, Permissions.ConsoleExecute);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            ConsoleCommandService sut = Service(db, new RecordingCoordinator(), new CapturingAuditWriter());

            ConsoleExecutionResult result = await sut.ExecuteAsync(user, unknown, "players");

            await Assert.That(result.Failure).IsEqualTo(ConsoleCommandFailure.ServerNotFound);
        });
    }

    [Test]
    public async Task Execute_rejects_unsafe_input_without_enqueuing_or_auditing()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ConsoleExecute);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ConsoleCommandService sut = Service(db, coordinator, audit);

            ConsoleExecutionResult result = await sut.ExecuteAsync(user, serverId, "save\nquit");

            await Assert.That(result.Failure).IsEqualTo(ConsoleCommandFailure.InvalidInput);
            await Assert.That(result.Detail).IsNotNull();
            await Assert.That(coordinator.LastRequest).IsNull();
            await Assert.That(audit.Entries).IsEmpty();
        });
    }

    [Test]
    public async Task Execute_denies_a_policy_blocked_command_audits_it_and_does_not_enqueue()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ConsoleExecute);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            CapturingAuditWriter audit = new();
            ConsoleCommandService sut = Service(db, coordinator, audit);

            ConsoleExecutionResult result = await sut.ExecuteAsync(user, serverId, "setpassword \"bob\" \"pw\"");

            await Assert.That(result.Failure).IsEqualTo(ConsoleCommandFailure.Denied);
            await Assert.That(coordinator.LastRequest).IsNull();
            await Assert.That(audit.Actions).Contains(ConsoleAuditActions.CommandDenied);
            await Assert.That(audit.Entries[0].Outcome).IsEqualTo(AuditOutcome.Denied);
        });
    }

    [Test]
    public async Task Execute_denies_quit_because_it_bypasses_the_safe_stop()
    {
        await WithSqlite(async options =>
        {
            UserId user = UserId.New();
            AgentId agent = AgentId.New();
            ServerId serverId = await SeedServerAsync(options, agent);
            await SeedAssignmentAsync(options, user, serverId, Permissions.ConsoleExecute);

            await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
            RecordingCoordinator coordinator = new();
            ConsoleCommandService sut = Service(db, coordinator, new CapturingAuditWriter());

            ConsoleExecutionResult result = await sut.ExecuteAsync(user, serverId, "quit");

            await Assert.That(result.Failure).IsEqualTo(ConsoleCommandFailure.Denied);
            await Assert.That(coordinator.LastRequest).IsNull();
        });
    }

    private static ConsoleCommandService Service(
        ZWardenDbContext db,
        RecordingCoordinator coordinator,
        CapturingAuditWriter audit)
        => new(
            new ServerRepository(db),
            new PermissionChecker(db, new TestTenantContext(Tenant)),
            coordinator,
            audit);

    private sealed class RecordingCoordinator : IOperationCoordinator
    {
        public EnqueueOperationRequest? LastRequest { get; private set; }

        public Task<Operation> EnqueueAsync(
            EnqueueOperationRequest request,
            UserId? actor = null,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(Operation.Enqueue(
                request.AgentId, request.Kind, request.IsMutating, request.IdempotencyKey, Now, request.ServerId,
                request.CommandPayload));
        }

        public Task<Operation> RequestCancellationAsync(
            OperationId operationId,
            UserId? actor = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static async Task<ServerId> SeedServerAsync(DbContextOptions options, AgentId agent)
    {
        await using ZWardenDbContext db = new(options, new TestTenantContext(Tenant));
        Server server = Server.Import(agent, ServerId.New(), "survivors", Now);
        new ServerRepository(db).Add(server);
        await db.SaveChangesAsync();
        return server.Id;
    }

    private static async Task SeedAssignmentAsync(
        DbContextOptions options,
        UserId user,
        ServerId server,
        PermissionDefinition permission)
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
