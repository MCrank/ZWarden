namespace ZWarden.Web.Components.Servers;

/// <summary>The one operator-facing reason a server-changing action was refused because the server's host isn't
/// connected (#383). Every page, sheet and endpoint that maps a host-offline failure shows this text.</summary>
public static class HostOfflineText
{
    public const string Message = "That server's host is offline. Try again when it reconnects.";

    public const string DeployMessage = "That host is offline. Pick a connected host, or try again when it reconnects.";
}
