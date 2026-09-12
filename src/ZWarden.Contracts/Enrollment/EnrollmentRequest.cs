namespace ZWarden.Contracts.Enrollment;

/// <summary>
/// The body an Agent posts to the enrollment exchange (F9; ADR 0007): the one-time enrollment secret it was
/// given. This is the <b>pre-trust bootstrap</b> that establishes the Web ↔ Agent boundary, not a message
/// that crosses the closed command/event vocabulary — so it is a plain contract, deliberately <b>not</b> an
/// <c>AgentCommand</c>/<c>AgentEvent</c> and carrying no <c>[ProtocolMessage]</c>. The secret is a plain
/// string on the wire (the Agent presents it); it is wrapped in a secret-aware type off the wire.
/// </summary>
public sealed record EnrollmentRequest(string EnrollmentSecret);
