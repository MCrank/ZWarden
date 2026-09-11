namespace ZWarden.Web.Components.Ui;

/// <summary>
/// A UI-local status enum used only to demonstrate the StatusBadge wrapper seam in
/// Feature 0. The authoritative hierarchical health model - stopped, starting,
/// healthy, degraded, failed - is Feature 16's domain, not the Web layer's.
/// </summary>
public enum ServerStatus
{
    Unknown,
    Healthy,
    Degraded,
    Unhealthy,
}
