namespace ZWarden.Contracts.Enrollment;

/// <summary>
/// The successful enrollment-exchange result returned to the Agent (F9; ADR 0007): the new Agent id
/// (<c>agt-…</c>), the per-Agent credential shown exactly once, and the optional operator label. A plain
/// contract, not part of the closed protocol vocabulary (see <see cref="EnrollmentRequest"/>). Ids and the
/// credential are plain strings on the wire; the Agent wraps the credential in a secret-aware type and
/// stores it immediately. A failed exchange returns no body — only a generic status, so the endpoint is not
/// an oracle.
/// </summary>
public sealed record EnrollmentResponse(string AgentId, string AgentCredential, string? Label);
