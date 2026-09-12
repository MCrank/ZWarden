using ZWarden.Domain.Ids;
using ZWarden.Web.Agents;

namespace ZWarden.Web.Tests.Agents;

/// <summary>
/// F10 S4: the in-memory <see cref="AgentConnectionRegistry"/> — the per-process record of live connections
/// that F11 dispatches through and F9 revoke/disable aborts through. Register/lookup/remove, one connection
/// per Agent (a reconnect displaces and aborts the prior socket), and a targeted abort.
/// </summary>
public class AgentConnectionRegistryTests
{
    private static readonly AgentId Agent = AgentId.New();

    [Test]
    public async Task Register_makes_the_agent_connected_and_resolvable()
    {
        AgentConnectionRegistry registry = new();
        registry.Register(Agent, "conn-1", static () => { });

        await Assert.That(registry.IsConnected(Agent)).IsTrue();
        await Assert.That(registry.GetConnectionId(Agent)).IsEqualTo("conn-1");
    }

    [Test]
    public async Task Remove_clears_the_connection()
    {
        AgentConnectionRegistry registry = new();
        registry.Register(Agent, "conn-1", static () => { });

        registry.Remove("conn-1");

        await Assert.That(registry.IsConnected(Agent)).IsFalse();
        await Assert.That(registry.GetConnectionId(Agent)).IsNull();
    }

    [Test]
    public async Task A_reconnect_displaces_and_aborts_the_prior_connection()
    {
        AgentConnectionRegistry registry = new();
        bool firstAborted = false;
        registry.Register(Agent, "conn-1", () => firstAborted = true);

        registry.Register(Agent, "conn-2", static () => { });

        await Assert.That(firstAborted).IsTrue();
        await Assert.That(registry.GetConnectionId(Agent)).IsEqualTo("conn-2");
        // Removing the stale connection id must not evict the live one.
        registry.Remove("conn-1");
        await Assert.That(registry.IsConnected(Agent)).IsTrue();
    }

    [Test]
    public async Task TryAbort_aborts_and_clears_a_live_connection_and_reports_it()
    {
        AgentConnectionRegistry registry = new();
        bool aborted = false;
        registry.Register(Agent, "conn-1", () => aborted = true);

        await Assert.That(registry.TryAbort(Agent)).IsTrue();
        await Assert.That(aborted).IsTrue();
        await Assert.That(registry.IsConnected(Agent)).IsFalse();

        // A second abort has nothing to do.
        await Assert.That(registry.TryAbort(Agent)).IsFalse();
    }
}
