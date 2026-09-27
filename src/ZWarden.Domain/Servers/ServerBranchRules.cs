namespace ZWarden.Domain.Servers;

/// <summary>
/// The rules for the Build 42 Steam branch a server installs (#258), shared by the Web edge and the Agent. Null means
/// the public branch. A branch is fixed at create: F17 Update and #229 Recreate stay on it. The name ends up in
/// <c>-beta ${ZW_PZ_BETA}</c> in the SteamCMD runscript without quotes, so only a tight charset is accepted. Whether
/// the branch exists is not checked here: a nonexistent name fails the install closed in the container.
/// </summary>
public static class ServerBranchRules
{
    /// <summary>Steam's name for the default branch. Stored as null.</summary>
    public const string PublicBranch = "public";

    /// <summary>The longest branch name accepted.</summary>
    public const int MaxLength = 64;

    /// <summary>The Build 41 branch, refused: the settings catalog and mod checks assume Build 42.</summary>
    private const string Build41Branch = "legacy41";

    /// <summary>
    /// Trims and lowercases <paramref name="branch"/>. Returns null for no branch or <c>public</c>. Call
    /// <see cref="Validate"/> first; this doesn't reject anything.
    /// </summary>
    public static string? Normalize(string? branch)
    {
        var trimmed = branch?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) || trimmed == PublicBranch ? null : trimmed;
    }

    /// <summary>
    /// Validates a requested branch. Returns <c>null</c> when acceptable, or a short operator-facing reason why it
    /// was rejected.
    /// </summary>
    public static string? Validate(string? branch)
    {
        var normalized = Normalize(branch);
        if (normalized is null)
        {
            return null;
        }

        if (normalized.Length > MaxLength)
        {
            return $"The branch name must be at most {MaxLength} characters.";
        }

        if (normalized[0] is '-' or '.' || !normalized.All(IsBranchChar))
        {
            return "The branch name may only contain letters, digits, '.', '_' and '-', and must start with a letter or digit.";
        }

        return normalized == Build41Branch ? "ZWarden supports Build 42 only; Build 41 (legacy41) can't be installed." : null;
    }

    /// <summary>The name shown to the operator: the branch, or <c>public</c> for the default.</summary>
    public static string DisplayName(string? branch) => Normalize(branch) ?? PublicBranch;

    private static bool IsBranchChar(char c) => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-';
}

/// <summary>A branch offered in the new-server form.</summary>
/// <param name="Branch">The Steam branch name; null for public.</param>
/// <param name="Label">The short name shown in the picker.</param>
/// <param name="Description">What choosing it means for the operator.</param>
/// <param name="IsPreview">A pre-release branch: mods may break and a world may not survive going back.</param>
public sealed record ServerBranchOption(string? Branch, string Label, string Description, bool IsPreview = false);

/// <summary>
/// The curated Build 42 branch list shipped with ZWarden (#258), checked against Steam's <c>app_info_print 380870</c>
/// and updated per release. Anything else can be typed as a custom branch.
/// </summary>
public static class ServerBranchCatalog
{
    /// <summary>The branches offered, public first (the default).</summary>
    public static IReadOnlyList<ServerBranchOption> Options { get; } =
    [
        new(null, "Latest public", "The current stable Build 42. Updates follow each new public release."),
        new("unstable", "Unstable preview",
            "The newest 42.x before it goes public. Mods may break, and a world may not survive going back.",
            IsPreview: true),
        new("42.19", "Pinned 42.19", "Build 42.19.2. Stays on this version through updates."),
    ];
}
