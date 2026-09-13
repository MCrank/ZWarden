using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Operations;
using ZWarden.Web.Operations;

namespace ZWarden.Web.Tests.Operations;

/// <summary>
/// F15: the operation-kind → command map (<see cref="OperationDispatcher.CommandFor"/>) — one place that turns
/// each <see cref="OperationKind"/> into the payload-free command it dispatches. The lifecycle kinds map to the
/// lifecycle commands; the diagnostic/provisioning maps are unregressed; an unmapped kind throws rather than
/// dispatching a wrong command.
/// </summary>
public class OperationDispatcherMapTests
{
    [Test]
    public async Task Diagnostic_and_provisioning_kinds_map_to_their_commands()
    {
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.DiagnosticsPing)).IsTypeOf<PingAgent>();
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.DiagnosticsDockerHealth)).IsTypeOf<ProbeDockerHealth>();
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.ProvisionServer)).IsTypeOf<CreateServer>();
    }

    [Test]
    public async Task Lifecycle_kinds_map_to_the_lifecycle_commands()
    {
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.StartServer)).IsTypeOf<StartServer>();
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.StopServer)).IsTypeOf<StopServer>();
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.RestartServer)).IsTypeOf<RestartServer>();
    }

    [Test]
    public async Task The_update_kind_maps_to_the_update_command()
    {
        await Assert.That(OperationDispatcher.CommandFor(OperationKind.UpdateServer)).IsTypeOf<UpdateServer>();
    }

    [Test]
    public async Task An_unmapped_kind_throws()
    {
        await Assert.That(() => OperationDispatcher.CommandFor((OperationKind)999)).Throws<NotSupportedException>();
    }
}
