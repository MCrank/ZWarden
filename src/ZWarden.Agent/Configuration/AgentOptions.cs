using System.ComponentModel.DataAnnotations;

namespace ZWarden.Agent.Configuration;

/// <summary>
/// The Agent runtime's strongly-typed configuration (F8), bound from the <c>Agent</c> section and
/// validated at startup (<see cref="AgentOptionsValidator"/>, <c>ValidateOnStart</c>). Invalid
/// configuration stops the host before any work begins rather than starting an Agent in an
/// ambiguous state.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>The configuration section these options bind from.</summary>
    public const string SectionName = "Agent";

    /// <summary>
    /// Where the Agent persists its self-identity (F8). Defaults under the per-user local
    /// application-data directory. The identity is a stable <c>agt-</c> id, not a credential —
    /// trust is bound later by enrollment (F9).
    /// </summary>
    [Required]
    public string IdentityFilePath { get; set; } = DefaultIdentityFilePath();

    /// <summary>
    /// The ZWarden.Web control-plane endpoint the Agent will connect to. <b>Declared now for F10;
    /// F8 validates it but does not connect.</b> Must be an absolute <c>https</c>/<c>wss</c> URI.
    /// </summary>
    [Required]
    public string ControlPlaneUri { get; set; } = "https://localhost:8443";

    /// <summary>How often the Agent will emit a heartbeat once transport lands (F10). Must be positive.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How often the runtime re-evaluates and logs its liveness tick. Must be positive.</summary>
    public TimeSpan HealthReportInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>How long graceful shutdown may take before the host stops forcibly. Must be positive.</summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>The default identity-file location: <c>%LOCALAPPDATA%/ZWarden/Agent/agent-id.txt</c>.</summary>
    public static string DefaultIdentityFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ZWarden",
            "Agent",
            "agent-id.txt");
}
