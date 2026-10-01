using ZWarden.Application.Servers;

namespace ZWarden.Web.Components.Pages.Servers.Sections;

/// <summary>
/// A lifecycle action a section ran on the Server (#298) — e.g. Overview's Recreate or Delete — handed back to the
/// page, which shows it in the header's lifecycle alert and re-reads the header status.
/// </summary>
public readonly record struct LifecycleOutcome(string Command, ServerLifecycleResult Result);
