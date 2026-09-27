using ZWarden.Domain.Servers;

namespace ZWarden.Web.Components.Servers;

/// <summary>
/// The branch picker's parsing and wording (#258), shared by the new-server wizard, the fleet board and Server Detail.
/// The picker offers <see cref="ServerBranchCatalog.Options"/> plus <see cref="CustomChoice"/>. The static-SSR form
/// can't show or hide the custom field, so the typed name is read only when Custom is picked.
/// </summary>
public static class ServerBranchView
{
    /// <summary>The picker value that means "use the typed custom branch".</summary>
    public const string CustomChoice = "custom";

    /// <summary>The picker value for a catalog option: its branch, or empty for public.</summary>
    public static string ChoiceValue(ServerBranchOption option)
    {
        ArgumentNullException.ThrowIfNull(option);
        return option.Branch ?? string.Empty;
    }

    /// <summary>Resolves the picked branch. Returns <c>false</c> with an operator-facing <paramref name="error"/> for a
    /// value the picker doesn't offer, or an empty or invalid custom name; <c>true</c> with the normalized
    /// <paramref name="branch"/> (<c>null</c> for public) otherwise.</summary>
    public static bool TryResolve(string? choice, string? custom, out string? branch, out string? error)
    {
        branch = null;
        error = null;
        if (choice == CustomChoice)
        {
            if (string.IsNullOrWhiteSpace(custom))
            {
                error = "Enter the custom branch name, or pick a branch from the list.";
                return false;
            }

            error = ServerBranchRules.Validate(custom);
            branch = error is null ? ServerBranchRules.Normalize(custom) : null;
            return error is null;
        }

        string picked = choice ?? string.Empty;
        if (!ServerBranchCatalog.Options.Any(o => ChoiceValue(o) == picked))
        {
            error = "That branch is not offered.";
            return false;
        }

        branch = ServerBranchRules.Normalize(picked);
        return true;
    }

    /// <summary>The short name shown next to the version: the branch, with a preview flagged.</summary>
    public static string Label(string? branch) => IsPreview(branch)
        ? $"{ServerBranchRules.DisplayName(branch)} (preview)"
        : ServerBranchRules.DisplayName(branch);

    /// <summary>Whether <paramref name="branch"/> is a curated pre-release branch.</summary>
    public static bool IsPreview(string? branch) =>
        branch is not null && ServerBranchCatalog.Options.Any(o => o.Branch == branch && o.IsPreview);

    /// <summary>What an F17 Update does on this branch.</summary>
    public static string Note(string? branch) => branch is null
        ? "Updates follow each new public release."
        : $"Updates stay on {branch}. Changing branch needs a new server.";

    /// <summary>What the header's Update game pulls (#273): a version-named branch (<c>42.19</c>) is a pin that stays put;
    /// public and any other named branch move to its latest build.</summary>
    public static string UpdateNote(string? branch) => branch switch
    {
        null => "Pulls the latest build on public.",
        _ when IsPinned(branch) => $"Stays on {branch} (pinned).",
        _ => $"Pulls the latest build on {Label(branch)}.",
    };

    // A version-shaped branch name — digits separated by dots, e.g. 42.19 or 42.18.1.
    private static bool IsPinned(string branch) =>
        branch.Split('.') is { Length: >= 2 } parts && parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit));
}
