namespace ZWarden.Infrastructure.Configuration;

/// <summary>
/// The stable, machine-readable audit action names for configuration changes (F20b; F6, ADR 0019).
/// Server-scoped, actor-attributed, and carrying no secret — the values themselves live in the Configuration
/// Revision, not the audit detail.
/// </summary>
public static class ConfigurationAuditActions
{
    /// <summary>An operator requested a configuration apply (a mutating config Operation was enqueued — F20b).</summary>
    public const string Applied = "Server.ConfigurationApplied";

    /// <summary>An operator requested a restore of a prior configuration revision (F20b PR-4). The value edits
    /// that move the current state to the target revision were enqueued as a mutating config Operation.</summary>
    public const string Restored = "Server.ConfigurationRestored";

    /// <summary>An operator applied a whole-file raw edit to a configuration file (F20c PR-D). The text was staged
    /// to the Agent and a mutating raw-apply Operation was enqueued — a distinct, riskier surface than a surgical
    /// apply, so it carries its own audit action.</summary>
    public const string RawApplied = "Server.ConfigurationRawApplied";

    /// <summary>What became of the live reload after the Agent wrote an INI change (#225): Succeeded when RCON
    /// <c>reloadoptions</c> made it live, Failed when the server was running but the reload did not succeed. Written by
    /// the control plane on the Operation's completion; the detail says whether the change is live or waits for a
    /// restart.</summary>
    public const string LiveReload = "Server.ConfigurationLiveReload";
}
