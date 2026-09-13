using ZWarden.Domain.Ids;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// Sends interim <c>OperationProgress</c> for an in-flight Operation up the control-plane connection (F17). The
/// whole progress pipeline (Contracts → hub → store → operation state) existed since F11 but had no emitter;
/// F17's long SteamCMD update is its first user. Passed <i>into</i> command handling rather than injected, so
/// the <see cref="AgentCommandProcessor"/> stays transport-free and unit-testable against a fake reporter.
/// </summary>
public interface IOperationProgressReporter
{
    /// <summary>Reports progress for <paramref name="operationId"/>. Advisory and best-effort: a percentage is
    /// clamped by the consumer, and a send while disconnected is a no-op (the reaper is the safety net).</summary>
    Task ReportAsync(OperationId operationId, int percentComplete, string? statusLine, CancellationToken cancellationToken);
}

/// <summary>The no-op reporter used when no live connection is threading progress (e.g. a unit test, or a
/// command kind that reports no progress). A shared singleton — it holds no state.</summary>
public sealed class NullOperationProgressReporter : IOperationProgressReporter
{
    /// <summary>The shared instance.</summary>
    public static readonly NullOperationProgressReporter Instance = new();

    private NullOperationProgressReporter()
    {
    }

    /// <inheritdoc />
    public Task ReportAsync(OperationId operationId, int percentComplete, string? statusLine, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
