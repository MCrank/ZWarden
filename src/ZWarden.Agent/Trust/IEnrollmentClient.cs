using ZWarden.Domain.Security;

namespace ZWarden.Agent.Trust;

/// <summary>
/// Performs the pre-trust enrollment exchange against ZWarden.Web (F9; ADR 0007): presents the one-time
/// enrollment secret over HTTPS and returns the <see cref="AgentTrustMaterial"/> to store, or <c>null</c>
/// when the exchange is refused (the server returns one generic failure — no reason to act on). Enrollment
/// is a one-shot HTTPS POST, not the SignalR transport (that is F10).
/// </summary>
public interface IEnrollmentClient
{
    /// <summary>Exchanges <paramref name="enrollmentSecret"/> for trust material, or <c>null</c> on refusal.</summary>
    Task<AgentTrustMaterial?> EnrollAsync(SecretString enrollmentSecret, CancellationToken cancellationToken = default);
}
