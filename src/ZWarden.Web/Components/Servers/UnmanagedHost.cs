namespace ZWarden.Web.Components.Servers;

/// <summary>One Host's unmanaged containers for the Fleet adopt callout (#339): the Host's operator-facing name (#336)
/// and how many PZ containers it reports with no Server record. Serializable: it crosses into an interactive island.</summary>
public sealed record UnmanagedHost(string Name, int Count);
