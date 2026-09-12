using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;

namespace ZWarden.Application.Agents;

/// <summary>An operator-facing view of an <c>enr-</c> record — never its secret or hash.</summary>
public sealed record EnrollmentSummary(
    EnrollmentId Id,
    EnrollmentStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string? Label,
    AgentId? ConsumedByAgent);
