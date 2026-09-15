namespace ZWarden.Agent.Docker;

/// <summary>
/// One demultiplexed, newline-delimited line read from a container's log stream (F27). The thin engine emits
/// these <b>raw</b> — unsanitized, exactly as the daemon framed them — so a frame carries only which standard
/// stream it came from, the daemon-reported timestamp, and the line text. All ZWarden policy above it
/// (sanitization per PRD 38, monotonic sequencing, the wire <c>LogStream</c> enum) is the caller's job, which is
/// why this stays a plain Agent-side value with no dependency on <c>ZWarden.Contracts</c>.
/// </summary>
/// <param name="Timestamp">The daemon-reported time the line was written (its <c>?timestamps=1</c> prefix), or
/// the read time when that prefix is absent or unparseable.</param>
/// <param name="IsStderr"><see langword="true"/> when the line came from the container's stderr, else stdout.</param>
/// <param name="Text">The line, newline stripped, otherwise untouched.</param>
public readonly record struct ContainerLogFrame(DateTimeOffset Timestamp, bool IsStderr, string Text);
