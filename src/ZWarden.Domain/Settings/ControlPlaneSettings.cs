using System.Globalization;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Settings;

/// <summary>
/// A tenant's <b>control-plane settings</b> (<c>cps-</c>, #345): one row per tenant holding the settings an operator
/// edits from Settings rather than in deploy-time configuration. Every value is optional: null means the configured
/// value (or the built-in default) applies, so clearing a setting reverts to config. Typed columns, one per setting
/// (#346 and #347 add theirs), so each value is validated here rather than parsed by every reader.
/// <para>
/// It is <see cref="ITenantOwned"/> (stamped and filtered by the ambient tenant, ADR 0016) and <see cref="IVersioned"/>:
/// install-wide in a single-tenant self-host, per-customer in SaaS with no schema change.
/// </para>
/// </summary>
public sealed class ControlPlaneSettings : IVersioned, ITenantOwned
{
    /// <summary>The longest instance name accepted (characters, after trimming).</summary>
    public const int MaxInstanceNameLength = 64;

    /// <summary>EF / factory use.</summary>
    public ControlPlaneSettings()
    {
    }

    /// <summary>The settings identifier (<c>cps-&lt;uuid&gt;</c>).</summary>
    public ControlPlaneSettingsId Id { get; init; } = ControlPlaneSettingsId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The instance's display name, overriding <c>ZWarden:Instance:Name</c>; null when not overridden. Untrusted
    /// operator text, rendered only as text.</summary>
    public string? InstanceName { get; private set; }

    /// <inheritdoc />
    public Guid Version { get; set; }

    /// <summary>A tenant's settings with nothing overridden. The <see cref="TenantId"/> is left unset so the ownership
    /// interceptor stamps the ambient tenant on insert (ADR 0016).</summary>
    public static ControlPlaneSettings Create() => new() { Id = ControlPlaneSettingsId.New() };

    /// <summary>
    /// Sets the instance name: trimmed, 1-<see cref="MaxInstanceNameLength"/> characters, no control or format
    /// characters (a line break or zero-width character in a title or label). A null or blank name clears the override.
    /// </summary>
    /// <exception cref="ArgumentException">The name is too long or holds a control or format character.</exception>
    public void SetInstanceName(string? name)
    {
        string? trimmed = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (trimmed is not null)
        {
            if (trimmed.Length > MaxInstanceNameLength)
            {
                throw new ArgumentException(
                    $"The instance name must be at most {MaxInstanceNameLength} characters.", nameof(name));
            }

            if (trimmed.Any(c => char.IsControl(c) || char.GetUnicodeCategory(c) == UnicodeCategory.Format))
            {
                throw new ArgumentException("The instance name cannot contain control characters.", nameof(name));
            }
        }

        InstanceName = trimmed;
    }
}
