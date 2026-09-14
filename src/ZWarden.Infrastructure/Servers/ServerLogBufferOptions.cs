namespace ZWarden.Infrastructure.Servers;

/// <summary>
/// Bounds the in-memory <see cref="ServerLogBuffer"/> (F27). The buffer is the "bounded buffering" the feature
/// requires — a fixed tail per Server so a chatty or long-watched server cannot grow ZWarden.Web's memory without
/// limit. The oldest lines are evicted first once the cap is reached.
/// </summary>
public sealed class ServerLogBufferOptions
{
    /// <summary>The maximum log lines retained per Server partition (the live tail). Defaults to 2000.</summary>
    public int MaxLinesPerServer { get; set; } = 2000;
}
