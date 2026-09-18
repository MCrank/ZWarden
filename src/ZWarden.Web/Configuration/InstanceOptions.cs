namespace ZWarden.Web.Configuration;

/// <summary>
/// Deploy-time identity of this ZWarden control-plane instance (#160). A friendly label the operator sets in
/// configuration (or an orchestrator injects) so an install is recognisable in the shell chrome and on the
/// Settings page — nothing more. It is intentionally <b>not</b> a runtime-mutable setting or an identifier:
/// ZWarden treats infrastructure configuration as deploy-time truth (session lifetime, forwarded-header trust,
/// backup roots are all fixed at boot), and the instance's real identity is always its install-state / tenant
/// id behind the scenes, never this string. So it needs no uniqueness and no persistence.
/// </summary>
public sealed class InstanceOptions
{
    /// <summary>The configuration section (<c>ZWarden:Instance</c>) this binds from.</summary>
    public const string SectionName = "ZWarden:Instance";

    /// <summary>The display name shown for this instance. Defaults to the product name when unset.</summary>
    public string Name { get; set; } = "ZWarden";
}
