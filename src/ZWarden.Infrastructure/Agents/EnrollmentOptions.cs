namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// Configuration for enrollment token minting (PRD 63A — short-lived). An operator may override the
/// lifetime per token, bounded by <see cref="MaxLifetime"/>; omitting it uses <see cref="DefaultLifetime"/>.
/// </summary>
public sealed class EnrollmentOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Enrollment";

    /// <summary>The lifetime applied when a caller does not specify one.</summary>
    public TimeSpan DefaultLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The maximum lifetime an operator may request; keeps "short-lived" a hard bound.</summary>
    public TimeSpan MaxLifetime { get; set; } = TimeSpan.FromHours(24);
}
