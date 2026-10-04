namespace ZWarden.Web.Components.Ui;

/// <summary>A Host's chip on the Hosts page (#335): its Agent connection (Online / Unreachable) and, separately, a trust
/// state that stops it acting (Revoked / Disabled). Rendered by <see cref="StatusBadge"/> in the Signal status tones.</summary>
public enum HostStatus
{
    /// <summary>The Agent holds a live control-plane connection.</summary>
    Online,

    /// <summary>The Agent is not connected — unreachable, not necessarily down.</summary>
    Unreachable,

    /// <summary>The Agent's credential has been revoked.</summary>
    Revoked,

    /// <summary>An operator disabled the Agent.</summary>
    Disabled,
}
