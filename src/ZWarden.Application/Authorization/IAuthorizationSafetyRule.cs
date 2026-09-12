namespace ZWarden.Application.Authorization;

/// <summary>
/// An operational safety rule consulted as the last step of an authorization decision (PRD 12A: the
/// decision "must include … applicable safety rules"; ADR 0018). A rule is <b>deny-only</b>: it can veto an
/// otherwise-allowed decision but can never grant one. The checker consults rules only after a permission
/// grant has been established, so returning <see cref="AuthorizationDecision.Deny"/> blocks the action and
/// returning an allow simply passes.
/// </summary>
/// <remarks>
/// v1.0 ships the seam with no concrete rules registered (the default is no rule, so nothing is vetoed).
/// Concrete high-risk safeguards — confirmation, cooldowns, maintenance windows, two-person approval,
/// step-up auth, time-limited grants (PRD 12A) — bind to the operations that own them in later features.
/// </remarks>
public interface IAuthorizationSafetyRule
{
    /// <summary>Vetoes (<see cref="AuthorizationDecision.Deny"/>) or passes (an allow) the request.</summary>
    Task<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken cancellationToken = default);
}
