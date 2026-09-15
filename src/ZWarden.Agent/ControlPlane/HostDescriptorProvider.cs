using System.Runtime.InteropServices;
using ZWarden.Contracts.Protocol.Messages;

namespace ZWarden.Agent.ControlPlane;

/// <summary>
/// Builds the <see cref="HostDescriptor"/> this Agent self-reports on connect (F35 D-1) — the Host's machine
/// name, this Agent build's version, and a short OS-platform label — so a multi-Host fleet is legible in the
/// operator inventory. Computed once per process; the facts are stable for the Agent's lifetime.
/// </summary>
/// <remarks>
/// Every field is coalesced to a non-blank fallback: the descriptor rides <c>AgentHello</c> and the control
/// plane records it inside the connect handshake, where a blank value would be rejected and could tear the
/// connection. The facts are <b>observed, not trusted</b> (trust-boundaries.md §3) — display-only.
/// </remarks>
public static class HostDescriptorProvider
{
    /// <summary>The descriptor for the Host this Agent runs on, computed once.</summary>
    public static HostDescriptor Current { get; } = Build();

    private static HostDescriptor Build()
    {
        string hostname = Blank(Environment.MachineName) ? "unknown-host" : Environment.MachineName;
        string version = typeof(HostDescriptorProvider).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        return new HostDescriptor(hostname, version, OsPlatformLabel());
    }

    /// <summary>A short, stable OS-platform label, or <c>Unknown</c> on an unrecognized platform.</summary>
    internal static string OsPlatformLabel() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "Windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macOS"
        : RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) ? "FreeBSD"
        : "Unknown";

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
