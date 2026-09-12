namespace ZWarden.Contracts.Protocol;

/// <summary>
/// The single monotonic protocol version (ADR 0020). Every <see cref="Envelope{TPayload}"/> is
/// stamped with the version it was encoded under. <b>Bump <see cref="Current"/> on any change an
/// older peer would mis-read</b> (a removed or retyped member, a changed discriminator, a changed
/// meaning); a purely additive change — a new optional member, a new message leaf — may reuse the
/// current version, because the serializer tolerates unknown members.
/// </summary>
public static class ProtocolVersion
{
    /// <summary>The protocol version this build speaks. Starts at 1.</summary>
    public const int Current = 1;
}

/// <summary>
/// The band of protocol versions ZWarden.Web will accept from a connecting Agent — from
/// <see cref="MinSupported"/> (the oldest still understood) through <see cref="Current"/> (this
/// build's own). F7 owns this rule; F10 evaluates it at connect time against the Agent's
/// <see cref="AgentHello"/> envelope version.
/// </summary>
/// <param name="MinSupported">The oldest Agent protocol version still accepted.</param>
/// <param name="Current">This build's protocol version; the newest accepted.</param>
public readonly record struct ProtocolVersionRange(int MinSupported, int Current)
{
    /// <summary>The range this build supports: <c>[1, <see cref="ProtocolVersion.Current"/>]</c>.</summary>
    public static ProtocolVersionRange Supported => new(1, ProtocolVersion.Current);
}
