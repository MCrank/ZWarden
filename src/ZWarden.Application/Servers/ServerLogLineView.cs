using ZWarden.Domain.Ids;

namespace ZWarden.Application.Servers;

/// <summary>
/// One sanitized log line ZWarden.Web holds for a Server (F27), mapped from the wire batch by ZWarden.Web (the
/// Application layer references no Contracts). The stream origin is carried as <see cref="IsStderr"/> rather than
/// the wire enum for the same reason. <see cref="Text"/> was already sanitized on the Agent (PRD 38) and is still
/// rendered as data by the UI. Observed, transient, non-secret.
/// </summary>
/// <param name="Sequence">The Agent's per-Server monotonic line number — the live-tail cursor.</param>
/// <param name="Timestamp">When the line was written (UTC), as reported by the container.</param>
/// <param name="IsStderr"><see langword="true"/> when the line came from stderr, else stdout.</param>
/// <param name="Text">The sanitized line text.</param>
/// <param name="Truncated"><see langword="true"/> when the Agent's length cap cut the line.</param>
public sealed record ServerLogLineView(
    long Sequence,
    DateTimeOffset Timestamp,
    bool IsStderr,
    string Text,
    bool Truncated);

/// <summary>
/// A slice of a Server's log tail returned by <see cref="IServerLogBuffer"/> (F27): the lines newer than the
/// caller's cursor, and whether the Agent has coalesced any lines away under its rate cap since streaming began.
/// </summary>
/// <param name="Lines">The lines with a sequence greater than the requested cursor, in order.</param>
/// <param name="Dropped"><see langword="true"/> once the Agent has dropped any lines to a rate cap — a sticky,
/// informational flag the UI surfaces so the loss is visible rather than silent.</param>
public sealed record ServerLogSlice(IReadOnlyList<ServerLogLineView> Lines, bool Dropped);
