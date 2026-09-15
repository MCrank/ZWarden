namespace ZWarden.Infrastructure.Setup;

/// <summary>
/// A process-wide, monotonic memo of "setup is complete" (F33). The first-run gate runs on every request;
/// once setup is done it must not pay a database read forever after, so the first observation of completion
/// latches this singleton and every later request short-circuits on it. Completion never reverts in v1.0, so
/// a one-way <see langword="volatile"/> flag needs no lock.
/// </summary>
public sealed class SetupCompletionSignal
{
    private volatile bool _isComplete;

    /// <summary>True once setup has been observed complete in this process.</summary>
    public bool IsComplete => _isComplete;

    /// <summary>Latches completion. Idempotent and thread-safe.</summary>
    public void MarkComplete() => _isComplete = true;
}
