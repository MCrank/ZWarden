using ZWarden.Domain.Operations;
using ZWarden.Web.Time;

namespace ZWarden.Web.Components.Servers;

/// <summary>
/// The server page's "last action failed" alert (#266): the Server's most recent finished mutating Operation, when it
/// failed and nothing has succeeded since. Without it a fast refusal — a Recreate's port pre-flight, say — showed only
/// "enqueued", a moment of RECREATING, and the old state again: a silent no-op. The reason is Agent-authored,
/// non-secret and length-bounded by the domain, but still untrusted text (trust-boundaries.md §3): Razor escapes it and
/// the live-status script writes it with <c>textContent</c> only.
/// </summary>
/// <param name="OperationId">The failed Operation's id — the key a viewer's dismissal is remembered by.</param>
/// <param name="Action">The operator-facing action name, e.g. <c>Recreate</c>.</param>
/// <param name="Reason">The failure reason.</param>
/// <param name="At">When it failed, formatted in the operator's display time zone (#211).</param>
public sealed record ServerFailureView(string OperationId, string Action, string Reason, string At)
{
    /// <summary>Projects a failed Operation, or <c>null</c> when there is none.</summary>
    public static ServerFailureView? From(Operation? failed, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (failed is not { State: OperationState.Failed })
        {
            return null;
        }

        DateTimeOffset at = failed.CompletedAt ?? failed.EnqueuedAt;
        return new ServerFailureView(
            failed.Id.ToString(),
            ActionName(failed.Kind),
            string.IsNullOrWhiteSpace(failed.FailureReason) ? "No reason was reported." : failed.FailureReason,
            OperatorTimeZone.Format(at, zone));
    }

    /// <summary>The operator-facing name of a mutating action.</summary>
    public static string ActionName(OperationKind kind) => kind switch
    {
        OperationKind.ProvisionServer => "Provisioning",
        OperationKind.StartServer => "Start",
        OperationKind.StopServer => "Stop",
        OperationKind.RestartServer => "Restart",
        OperationKind.UpdateServer => "Update",
        OperationKind.RecreateServer => "Recreate",
        OperationKind.ConfigApply or OperationKind.ConfigApplyRaw => "Configuration change",
        OperationKind.Backup => "Backup",
        OperationKind.DeleteBackup => "Backup deletion",
        OperationKind.Restore => "Restore",
        OperationKind.KickPlayer => "Kick",
        OperationKind.BanPlayer => "Ban",
        OperationKind.UnbanPlayer => "Unban",
        OperationKind.RemoveFromWhitelist => "Whitelist removal",
        OperationKind.SetWhitelistMode => "Whitelist mode change",
        OperationKind.ExecuteConsoleCommand => "Console command",
        _ => kind.ToString(),
    };
}
