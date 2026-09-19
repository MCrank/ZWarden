namespace ZWarden.Agent.Trust;

/// <summary>
/// A one-shot signal that the Agent's enrollment has <b>settled</b> — reached a terminal outcome: already
/// enrolled at startup, enrolled now (inline or via the background retry, #185), refused, or no secret
/// configured. In every case the trust store will not change further without a restart. The F10 connection
/// step awaits this when it finds no trust material at startup, so an Agent that enrols <i>later</i> via the
/// background retry connects with no restart (#195). Set-once and idempotent: awaiting after it is set (or
/// calling <see cref="MarkSettled"/> again) completes immediately and has no further effect.
/// </summary>
public sealed class AgentEnrollmentSignal
{
    private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>True once enrollment has settled. Chiefly for tests; the connection step awaits instead.</summary>
    public bool IsSettled => _settled.Task.IsCompletedSuccessfully;

    /// <summary>Marks enrollment settled. Safe to call more than once — only the first call has effect.</summary>
    public void MarkSettled() => _settled.TrySetResult();

    /// <summary>
    /// Completes when enrollment has settled, or throws <see cref="OperationCanceledException"/> if
    /// <paramref name="cancellationToken"/> fires first (e.g. host shutdown) — so a caller that is waiting to
    /// connect unwinds cleanly rather than blocking forever when the Agent is never enrolled.
    /// </summary>
    public Task WaitUntilSettledAsync(CancellationToken cancellationToken) =>
        _settled.Task.WaitAsync(cancellationToken);
}
