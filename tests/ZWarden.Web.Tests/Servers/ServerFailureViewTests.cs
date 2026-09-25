using ZWarden.Domain.Ids;
using ZWarden.Domain.Operations;
using ZWarden.Web.Components.Servers;

namespace ZWarden.Web.Tests.Servers;

/// <summary>#266: the "last action failed" projection — only a failed Operation, named for the operator, in their zone.</summary>
public class ServerFailureViewTests
{
    private static readonly DateTimeOffset At = new(2026, 9, 25, 20, 30, 0, TimeSpan.Zero);

    private static Operation Failed(OperationKind kind, string reason)
    {
        Operation op = Operation.Enqueue(AgentId.New(), kind, isMutating: true, "k", At, ServerId.New());
        op.MarkDispatched(At.AddMinutes(5), At);
        op.Fail(reason, At);
        return op;
    }

    [Test]
    public async Task A_failed_recreate_projects_its_action_reason_and_time()
    {
        Operation op = Failed(OperationKind.RecreateServer, "Host port 16261/udp is already published by another container on this host.");

        ServerFailureView view = ServerFailureView.From(op, TimeZoneInfo.Utc)!;

        await Assert.That(view.OperationId).IsEqualTo(op.Id.ToString());
        await Assert.That(view.Action).IsEqualTo("Recreate");
        await Assert.That(view.Reason).Contains("16261/udp");
        await Assert.That(view.At).IsEqualTo("2026-09-25 20:30:00 UTC");
    }

    [Test]
    public async Task No_operation_or_one_that_did_not_fail_projects_nothing()
    {
        Operation succeeded = Operation.Enqueue(AgentId.New(), OperationKind.StartServer, isMutating: true, "k", At, ServerId.New());
        succeeded.MarkDispatched(At.AddMinutes(5), At);
        succeeded.Succeed(At);

        await Assert.That(ServerFailureView.From(null, TimeZoneInfo.Utc)).IsNull();
        await Assert.That(ServerFailureView.From(succeeded, TimeZoneInfo.Utc)).IsNull();
    }

    [Test]
    [Arguments(OperationKind.RestartServer, "Restart")]
    [Arguments(OperationKind.ConfigApplyRaw, "Configuration change")]
    [Arguments(OperationKind.ProvisionServer, "Provisioning")]
    public async Task Actions_are_named_for_the_operator(OperationKind kind, string expected)
    {
        await Assert.That(ServerFailureView.ActionName(kind)).IsEqualTo(expected);
    }
}
