using ZWarden.Domain;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Tests.Operations;

/// <summary>
/// F11 S1: the <see cref="Operation"/> aggregate (<c>op-</c>) and its closed state machine (ADR 0022) —
/// <c>Pending → Running → (Succeeded | Failed | Cancelled)</c> with <see cref="OperationState.Cancelling"/>
/// the one transient. Pure invariants here; the per-server lock, persistence and reaper are later slices.
/// </summary>
public class OperationTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 12, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Lease = Now.AddMinutes(2);

    private static Operation EnqueueMutating(string key = "idem-1")
        => Operation.Enqueue(AgentId.New(), OperationKind.DiagnosticsPing, isMutating: true, key, Now, ServerId.New());

    private static Operation Running()
    {
        Operation op = EnqueueMutating();
        op.MarkDispatched(Lease, Now.AddSeconds(1));
        return op;
    }

    [Test]
    public async Task Operation_is_tenant_owned_and_versioned()
    {
        await Assert.That(typeof(ITenantOwned).IsAssignableFrom(typeof(Operation))).IsTrue();
        await Assert.That(typeof(IVersioned).IsAssignableFrom(typeof(Operation))).IsTrue();
    }

    [Test]
    public async Task Enqueue_creates_a_pending_non_terminal_operation()
    {
        AgentId agent = AgentId.New();
        ServerId server = ServerId.New();
        Operation op = Operation.Enqueue(agent, OperationKind.DiagnosticsPing, isMutating: false, "idem-42", Now, server);

        await Assert.That(op.Id.IsEmpty).IsFalse();
        await Assert.That(op.AgentId).IsEqualTo(agent);
        await Assert.That(op.ServerId).IsEqualTo(server);
        await Assert.That(op.Kind).IsEqualTo(OperationKind.DiagnosticsPing);
        await Assert.That(op.IsMutating).IsFalse();
        await Assert.That(op.IdempotencyKey).IsEqualTo("idem-42");
        await Assert.That(op.State).IsEqualTo(OperationState.Pending);
        await Assert.That(op.EnqueuedAt).IsEqualTo(Now);
        await Assert.That(op.StartedAt).IsNull();
        await Assert.That(op.CompletedAt).IsNull();
        await Assert.That(op.LeaseExpiresAt).IsNull();
        await Assert.That(op.PercentComplete).IsEqualTo(0);
        await Assert.That(op.IsTerminal).IsFalse();
    }

    [Test]
    public async Task A_diagnostic_ping_is_agent_scoped_with_no_server()
    {
        AgentId agent = AgentId.New();
        Operation op = Operation.Enqueue(agent, OperationKind.DiagnosticsPing, isMutating: false, "ping-1", Now);

        await Assert.That(op.AgentId).IsEqualTo(agent);
        await Assert.That(op.ServerId).IsNull();
        await Assert.That(op.IsMutating).IsFalse();
    }

    [Test]
    public async Task Enqueue_rejects_a_blank_idempotency_key()
    {
        await Assert.That(() => Operation.Enqueue(AgentId.New(), OperationKind.DiagnosticsPing, true, "  ", Now, ServerId.New()))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Enqueue_rejects_an_empty_agent()
    {
        await Assert.That(() => Operation.Enqueue(default, OperationKind.DiagnosticsPing, false, "k", Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task Enqueue_rejects_a_mutating_operation_with_no_server()
    {
        await Assert.That(() => Operation.Enqueue(AgentId.New(), OperationKind.DiagnosticsPing, isMutating: true, "k", Now))
            .Throws<ArgumentException>();
    }

    [Test]
    public async Task MarkDispatched_moves_pending_to_running_under_a_lease()
    {
        Operation op = EnqueueMutating();

        op.MarkDispatched(Lease, Now.AddSeconds(1));

        await Assert.That(op.State).IsEqualTo(OperationState.Running);
        await Assert.That(op.StartedAt).IsEqualTo(Now.AddSeconds(1));
        await Assert.That(op.LeaseExpiresAt).IsEqualTo(Lease);
        await Assert.That(op.LastProgressAt).IsEqualTo(Now.AddSeconds(1));
        await Assert.That(op.IsTerminal).IsFalse();
    }

    [Test]
    public async Task MarkDispatched_is_illegal_from_any_state_but_pending()
    {
        Operation op = Running();
        await Assert.That(() => op.MarkDispatched(Lease, Now))
            .Throws<InvalidOperationStateTransitionException>();
    }

    [Test]
    public async Task ReportProgress_clamps_percentage_truncates_status_and_extends_the_lease()
    {
        Operation op = Running();
        string longLine = new('x', Operation.MaxReportedTextLength + 50);
        DateTimeOffset newLease = Lease.AddMinutes(2);

        op.ReportProgress(150, longLine, newLease, Now.AddSeconds(30));

        await Assert.That(op.PercentComplete).IsEqualTo(100);
        await Assert.That(op.StatusLine!.Length).IsEqualTo(Operation.MaxReportedTextLength);
        await Assert.That(op.LeaseExpiresAt).IsEqualTo(newLease);
        await Assert.That(op.LastProgressAt).IsEqualTo(Now.AddSeconds(30));
        await Assert.That(op.State).IsEqualTo(OperationState.Running);
    }

    [Test]
    public async Task ReportProgress_clamps_a_negative_percentage_to_zero()
    {
        Operation op = Running();
        op.ReportProgress(-5, "starting", Lease, Now.AddSeconds(5));
        await Assert.That(op.PercentComplete).IsEqualTo(0);
    }

    [Test]
    public async Task ReportProgress_is_illegal_on_a_terminal_operation()
    {
        Operation op = Running();
        op.Succeed(Now.AddMinutes(1));
        await Assert.That(() => op.ReportProgress(50, null, Lease, Now))
            .Throws<InvalidOperationStateTransitionException>();
    }

    [Test]
    public async Task Succeed_from_running_is_terminal_and_forces_full_progress()
    {
        Operation op = Running();

        op.Succeed(Now.AddMinutes(1));

        await Assert.That(op.State).IsEqualTo(OperationState.Succeeded);
        await Assert.That(op.PercentComplete).IsEqualTo(100);
        await Assert.That(op.CompletedAt).IsEqualTo(Now.AddMinutes(1));
        await Assert.That(op.LeaseExpiresAt).IsNull();
        await Assert.That(op.IsTerminal).IsTrue();
    }

    [Test]
    public async Task Fail_from_running_records_the_reason_and_clears_the_lease()
    {
        Operation op = Running();

        op.Fail("lease expired — agent did not report completion", Now.AddMinutes(3));

        await Assert.That(op.State).IsEqualTo(OperationState.Failed);
        await Assert.That(op.FailureReason).IsEqualTo("lease expired — agent did not report completion");
        await Assert.That(op.CompletedAt).IsEqualTo(Now.AddMinutes(3));
        await Assert.That(op.LeaseExpiresAt).IsNull();
        await Assert.That(op.IsTerminal).IsTrue();
    }

    [Test]
    public async Task Fail_is_legal_from_pending_for_an_enqueue_timeout()
    {
        Operation op = EnqueueMutating();
        op.Fail("enqueue window elapsed", Now.AddMinutes(10));
        await Assert.That(op.State).IsEqualTo(OperationState.Failed);
    }

    [Test]
    public async Task Fail_rejects_a_blank_reason()
    {
        Operation op = Running();
        await Assert.That(() => op.Fail("  ", Now)).Throws<ArgumentException>();
    }

    [Test]
    public async Task Fail_is_illegal_on_a_terminal_operation()
    {
        Operation op = Running();
        op.Succeed(Now.AddMinutes(1));
        await Assert.That(() => op.Fail("too late", Now.AddMinutes(2)))
            .Throws<InvalidOperationStateTransitionException>();
    }

    [Test]
    public async Task RequestCancel_on_a_pending_operation_cancels_it_immediately()
    {
        Operation op = EnqueueMutating();

        op.RequestCancel(Now.AddSeconds(5));

        await Assert.That(op.State).IsEqualTo(OperationState.Cancelled);
        await Assert.That(op.CompletedAt).IsEqualTo(Now.AddSeconds(5));
        await Assert.That(op.IsTerminal).IsTrue();
    }

    [Test]
    public async Task RequestCancel_on_a_running_operation_enters_cancelling()
    {
        Operation op = Running();

        op.RequestCancel(Now.AddMinutes(1));

        await Assert.That(op.State).IsEqualTo(OperationState.Cancelling);
        await Assert.That(op.IsTerminal).IsFalse();
        await Assert.That(op.CompletedAt).IsNull();
    }

    [Test]
    public async Task RequestCancel_while_cancelling_is_a_no_op()
    {
        Operation op = Running();
        op.RequestCancel(Now.AddMinutes(1));

        op.RequestCancel(Now.AddMinutes(2));

        await Assert.That(op.State).IsEqualTo(OperationState.Cancelling);
    }

    [Test]
    public async Task RequestCancel_is_illegal_on_a_terminal_operation()
    {
        Operation op = Running();
        op.Succeed(Now.AddMinutes(1));
        await Assert.That(() => op.RequestCancel(Now.AddMinutes(2)))
            .Throws<InvalidOperationStateTransitionException>();
    }

    [Test]
    public async Task Cancel_completes_a_cancelling_operation()
    {
        Operation op = Running();
        op.RequestCancel(Now.AddMinutes(1));

        op.Cancel(Now.AddMinutes(2));

        await Assert.That(op.State).IsEqualTo(OperationState.Cancelled);
        await Assert.That(op.CompletedAt).IsEqualTo(Now.AddMinutes(2));
        await Assert.That(op.LeaseExpiresAt).IsNull();
        await Assert.That(op.IsTerminal).IsTrue();
    }

    [Test]
    public async Task Cancel_is_illegal_unless_cancelling()
    {
        Operation op = Running();
        await Assert.That(() => op.Cancel(Now.AddMinutes(1)))
            .Throws<InvalidOperationStateTransitionException>();
    }

    [Test]
    public async Task Succeed_can_win_a_race_against_a_requested_cancellation()
    {
        Operation op = Running();
        op.RequestCancel(Now.AddMinutes(1));

        op.Succeed(Now.AddMinutes(2));

        await Assert.That(op.State).IsEqualTo(OperationState.Succeeded);
        await Assert.That(op.IsTerminal).IsTrue();
    }
}
