using ZWarden.Domain.Ids;

namespace ZWarden.Application.Agents;

/// <summary>An operator-facing view of a trusted <c>agt-</c> record — never its credential or hash.
/// <see cref="HasCredential"/> is false once the credential is revoked.</summary>
public sealed record AgentSummary(
    AgentId Id,
    bool IsEnabled,
    bool HasCredential,
    DateTimeOffset EnrolledAt,
    DateTimeOffset CredentialRotatedAt,
    string? Label);
