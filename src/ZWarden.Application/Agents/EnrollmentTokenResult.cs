using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;

namespace ZWarden.Application.Agents;

/// <summary>
/// The result of minting an enrollment token. The one-time <see cref="Secret"/> is shown to the operator
/// exactly once and never stored raw (decision 2) — only its hash lives on the <c>enr-</c> record.
/// </summary>
public sealed record EnrollmentTokenResult(
    EnrollmentId EnrollmentId,
    SecretString Secret,
    DateTimeOffset ExpiresAt,
    string? Label);
