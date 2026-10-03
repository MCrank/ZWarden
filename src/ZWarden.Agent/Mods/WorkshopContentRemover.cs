using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ZWarden.Agent.Configuration;
using ZWarden.Agent.Docker;
using ZWarden.Agent.SteamCmd;
using ZWarden.Contracts.Protocol.Messages;
using ZWarden.Domain.Ids;

namespace ZWarden.Agent.Mods;

/// <summary>How a Workshop-content deletion ended: the per-id result, or why nothing was deleted.</summary>
/// <param name="Succeeded">Whether the request was carried out (individual ids may still have been refused).</param>
/// <param name="FailureReason">On failure, the Agent-authored reason; otherwise <c>null</c>.</param>
/// <param name="Result">On success, each requested id's outcome; otherwise <c>null</c>.</param>
public sealed record WorkshopContentRemoval(bool Succeeded, string? FailureReason, WorkshopContentDeletionResult? Result)
{
    internal static WorkshopContentRemoval Failure(string reason) => new(false, reason, null);
}

/// <summary>
/// Deletes unused Workshop downloads from a Server's install volume (#293). Fail-closed throughout: the Server must
/// have a container this Agent owns; every id must be a bare numeric Workshop id, or nothing is touched; the live
/// <c>WorkshopItems=</c> must be readable, and an id it lists is refused; and a folder that is (or sits under) a link
/// is refused, so a recursive delete never leaves <c>content/108600/</c>. An absent folder is already deleted.
/// </summary>
public interface IWorkshopContentRemover
{
    /// <summary>Deletes the folders of <paramref name="workshopIds"/> for <paramref name="serverId"/>.</summary>
    Task<WorkshopContentRemoval> DeleteAsync(
        ServerId serverId, IReadOnlyList<string> workshopIds, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IWorkshopContentRemover" />
public sealed partial class WorkshopContentRemover : IWorkshopContentRemover
{
    private readonly IServerInstallPaths _paths;
    private readonly ServerModConfigReader _config;
    private readonly IContainerRuntime _containers;
    private readonly ILogger<WorkshopContentRemover> _logger;

    public WorkshopContentRemover(
        IServerInstallPaths paths,
        ServerModConfigReader config,
        IContainerRuntime containers,
        ILogger<WorkshopContentRemover> logger)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(containers);
        ArgumentNullException.ThrowIfNull(logger);
        _paths = paths;
        _config = config;
        _containers = containers;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkshopContentRemoval> DeleteAsync(
        ServerId serverId, IReadOnlyList<string> workshopIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workshopIds);
        if (workshopIds.Count == 0)
        {
            return WorkshopContentRemoval.Failure("No Workshop items were named.");
        }

        if (workshopIds.FirstOrDefault(id => id is null || !WorkshopIdPattern().IsMatch(id)) is { } bad)
        {
            return WorkshopContentRemoval.Failure($"'{bad}' is not a Workshop item id.");
        }

        // The listing is scoped to containers this Agent owns (ContainerOwnershipGuard), so absence = not ours.
        IReadOnlyList<ManagedContainer> owned = await _containers.ListManagedAsync(cancellationToken).ConfigureAwait(false);
        if (!owned.Any(c => c.ServerId == serverId))
        {
            LogNotOwned(serverId);
            return WorkshopContentRemoval.Failure("This Agent owns no container for the server.");
        }

        ServerModConfig config = await _config.ReadAsync(serverId, cancellationToken).ConfigureAwait(false);
        if (config.Status is not ServerModConfigStatus.Read)
        {
            LogConfigNotRead(serverId, config.Status);
            return WorkshopContentRemoval.Failure(
                "The server's WorkshopItems= list could not be read, so nothing was deleted.");
        }

        HashSet<string> referenced = new(config.WorkshopIds, StringComparer.Ordinal);
        string root = Path.GetFullPath(_paths.GetWorkshopContentRoot(serverId));
        // The install volume (<serverId>.server) is four levels above steamapps/workshop/content/108600.
        string installRoot = Path.GetFullPath(Path.Combine(root, "..", "..", "..", ".."));
        List<WorkshopContentDeletion> items = [];
        foreach (string id in workshopIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorkshopContentDeletionOutcome outcome;
            try
            {
                outcome = referenced.Contains(id) ? WorkshopContentDeletionOutcome.RefusedReferenced : Delete(root, installRoot, id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LogDeleteFailed(serverId, id, ex.Message);
                return WorkshopContentRemoval.Failure($"Workshop item {id} could not be deleted: {ex.Message}");
            }

            LogOutcome(serverId, id, outcome);
            items.Add(new WorkshopContentDeletion(id, outcome));
        }

        return new WorkshopContentRemoval(true, null, new WorkshopContentDeletionResult(items));
    }

    private static WorkshopContentDeletionOutcome Delete(string root, string installRoot, string id)
    {
        string target = Path.GetFullPath(Path.Combine(root, id));
        if (!target.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return WorkshopContentDeletionOutcome.RefusedUnsafe;
        }

        if (File.Exists(target))
        {
            return WorkshopContentDeletionOutcome.RefusedUnsafe; // A file, not an item folder — not ours to judge.
        }

        if (!Directory.Exists(target) && !IsLink(target))
        {
            return WorkshopContentDeletionOutcome.AlreadyAbsent;
        }

        // GetFullPath doesn't resolve links: refuse if the item folder, or any folder between it and the install
        // volume (steamapps/workshop/content/108600), is one — the delete would land somewhere else.
        for (string? dir = target; dir is not null && dir.Length > installRoot.Length; dir = Path.GetDirectoryName(dir))
        {
            if (IsLink(dir))
            {
                return WorkshopContentDeletionOutcome.RefusedUnsafe;
            }
        }

        Directory.Delete(target, recursive: true);
        return WorkshopContentDeletionOutcome.Deleted;
    }

    // A symlink (dangling or not) has a LinkTarget; a Windows junction shows as a reparse point.
    private static bool IsLink(string path)
    {
        var info = new DirectoryInfo(path);
        return info.LinkTarget is not null || (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    // A Steam published-file id: a 64-bit number, so at most 20 digits.
    [GeneratedRegex("^[0-9]{1,20}$", RegexOptions.CultureInvariant)]
    private static partial Regex WorkshopIdPattern();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workshop delete for server {ServerId} refused: no owned container.")]
    private partial void LogNotOwned(ServerId serverId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workshop delete for server {ServerId} refused: config {Status}.")]
    private partial void LogConfigNotRead(ServerId serverId, ServerModConfigStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Workshop delete for server {ServerId}: item {WorkshopId} {Outcome}.")]
    private partial void LogOutcome(ServerId serverId, string workshopId, WorkshopContentDeletionOutcome outcome);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Workshop delete for server {ServerId}: item {WorkshopId} failed: {Reason}")]
    private partial void LogDeleteFailed(ServerId serverId, string workshopId, string reason);
}
