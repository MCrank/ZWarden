using ZWarden.Domain.Ids;
using ZWarden.Domain.Servers;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Servers;

/// <summary>
/// F14 S1 (PR-A): the <see cref="Server"/> aggregate (<c>srv-</c>) — one Project Zomboid instance under
/// ZWarden's management (CONTEXT.md). A tenant-owned, versioned record that belongs to exactly one Agent
/// (the Agent-to-Server association) and carries the coarse <b>last-reported</b> run-state, observed and
/// never inferred (trust-boundaries §3). The hierarchical health model is F16's; F14 stores only the
/// last observation. Pure invariants here.
/// </summary>
public class ServerTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

    private static Server Import(string name = "alpha") =>
        Server.Import(AgentId.New(), ServerId.New(), name, Now);

    [Test]
    public async Task Server_is_tenant_owned_and_versioned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(Server))).IsTrue();
        await Assert.That(typeof(IVersioned).IsAssignableFrom(typeof(Server))).IsTrue();
    }

    [Test]
    public async Task Import_adopts_the_discovered_id_and_binds_the_agent()
    {
        AgentId agent = AgentId.New();
        ServerId discovered = ServerId.New();

        Server server = Server.Import(agent, discovered, "survivors-1", Now, "the co-op world");

        // Import adopts the id the discovered container already carries (io.zwarden.server-id).
        await Assert.That(server.Id).IsEqualTo(discovered);
        await Assert.That(server.AgentId).IsEqualTo(agent);
        await Assert.That(server.Name).IsEqualTo("survivors-1");
        await Assert.That(server.Description).IsEqualTo("the co-op world");
        await Assert.That(server.CreatedAt).IsEqualTo(Now);
    }

    [Test]
    public async Task Import_leaves_the_tenant_unset_for_the_ownership_interceptor()
    {
        // ADR 0016: TenantId is stamped from the ambient tenant on insert, never chosen by the factory.
        Server server = Import();

        await Assert.That(server.TenantId.IsEmpty).IsTrue();
    }

    [Test]
    public async Task An_imported_server_starts_unknown_and_never_reported()
    {
        Server server = Import();

        await Assert.That(server.LastRunState).IsEqualTo(ServerRunState.Unknown);
        await Assert.That(server.LastStateReportedAt).IsNull();
        await Assert.That(server.GamePort).IsNull();
        await Assert.That(server.QueryPort).IsNull();
        await Assert.That(server.DockerContainerId).IsNull();
    }

    [Test]
    public async Task Import_rejects_a_blank_name()
    {
        await Assert.That(() => Server.Import(AgentId.New(), ServerId.New(), " ", Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Import_rejects_an_empty_agent_id()
    {
        await Assert.That(() => Server.Import(default, ServerId.New(), "alpha", Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Import_rejects_an_empty_server_id()
    {
        await Assert.That(() => Server.Import(AgentId.New(), default, "alpha", Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Register_mints_a_new_id_and_binds_the_agent_in_unknown_state()
    {
        AgentId agent = AgentId.New();

        Server server = Server.Register(agent, "survivors-new", Now, "fresh");

        await Assert.That(server.Id.IsEmpty).IsFalse();
        await Assert.That(server.AgentId).IsEqualTo(agent);
        await Assert.That(server.Name).IsEqualTo("survivors-new");
        await Assert.That(server.Description).IsEqualTo("fresh");
        await Assert.That(server.LastRunState).IsEqualTo(ServerRunState.Unknown);
        await Assert.That(server.TenantId.IsEmpty).IsTrue();
        await Assert.That(server.CreatedAt).IsEqualTo(Now);
    }

    [Test]
    public async Task Register_rejects_a_blank_name_and_an_empty_agent()
    {
        await Assert.That(() => Server.Register(AgentId.New(), " ", Now)).Throws<ArgumentException>();
        await Assert.That(() => Server.Register(default, "alpha", Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task RecordObservedState_updates_the_state_and_stamps_the_time()
    {
        Server server = Import();

        server.RecordObservedState(ServerRunState.Running, Now.AddMinutes(5));

        await Assert.That(server.LastRunState).IsEqualTo(ServerRunState.Running);
        await Assert.That(server.LastStateReportedAt).IsEqualTo(Now.AddMinutes(5));
    }

    [Test]
    public async Task RecordContainer_sets_the_ports_and_the_observed_docker_id()
    {
        Server server = Import();

        server.RecordContainer("c0ffee", gamePort: 16261, queryPort: 16262);

        await Assert.That(server.DockerContainerId).IsEqualTo("c0ffee");
        await Assert.That(server.GamePort).IsEqualTo(16261);
        await Assert.That(server.QueryPort).IsEqualTo(16262);
    }

    [Test]
    public async Task RecordContainer_rejects_out_of_range_ports()
    {
        Server server = Import();

        await Assert.That(() => server.RecordContainer("c0ffee", gamePort: 0, queryPort: 16262))
            .Throws<ArgumentOutOfRangeException>();
        await Assert.That(() => server.RecordContainer("c0ffee", gamePort: 16261, queryPort: 70000))
            .Throws<ArgumentOutOfRangeException>();
    }

    [Test]
    public async Task RecordContainer_rejects_identical_game_and_query_ports()
    {
        Server server = Import();

        await Assert.That(() => server.RecordContainer("c0ffee", gamePort: 16261, queryPort: 16261))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task RecordContainer_rejects_a_blank_docker_id()
    {
        Server server = Import();

        await Assert.That(() => server.RecordContainer(" ", gamePort: 16261, queryPort: 16262))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Rename_changes_the_name_and_description()
    {
        Server server = Import("old");

        server.Rename("new", "a fresh label");

        await Assert.That(server.Name).IsEqualTo("new");
        await Assert.That(server.Description).IsEqualTo("a fresh label");
    }

    [Test]
    public async Task Rename_rejects_a_blank_name()
    {
        Server server = Import();

        await Assert.That(() => server.Rename("  ", null)).Throws<ArgumentException>();
    }
}
