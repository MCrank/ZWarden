using ZWarden.Domain.Ids;

namespace ZWarden.Application.Authorization;

/// <summary>
/// A resource that authorization can be evaluated against a specific Server (ADR 0018). A resource passed
/// to <c>IAuthorizationService.AuthorizeAsync(user, resource, policy)</c> that implements this lets the
/// server-scoped handler narrow the decision to <see cref="ServerId"/> — matching PRD 12A's "target
/// resource + ownership/tenant scope" term. Business logic checks the same thing directly via
/// <see cref="IPermissionChecker.EvaluateAsync"/> with a Server.
/// </summary>
public interface IServerScoped
{
    /// <summary>The Server the decision is scoped to.</summary>
    ServerId ServerId { get; }
}
