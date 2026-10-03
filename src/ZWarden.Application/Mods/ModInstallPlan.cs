using ZWarden.Application.Workshop;
using ZWarden.Domain.Mods;

namespace ZWarden.Application.Mods;

/// <summary>How many mod ids the Workshop description offers for one-click Install (#291 D2).</summary>
public enum ModInstallKind
{
    /// <summary>The description lists none: Install writes <c>WorkshopItems=</c> only, and the item asks to Pick
    /// parts once <c>mod.info</c> is on disk after the next boot.</summary>
    NoIds,

    /// <summary>Exactly one id: Install enables it.</summary>
    OneId,

    /// <summary>Several ids: the operator chooses in the part picker (all pre-ticked) before anything is written.</summary>
    Choose,
}

/// <summary>
/// What one-click Install (#291) offers for a Workshop item: the mod ids its description lists, validated as
/// <see cref="PzModId"/>s (the text is untrusted), and how to present them. Advisory only — Install re-validates
/// whatever ids the operator submits, and <c>mod.info</c> after the boot is the truth that flags a wrong guess.
/// Enabling a guess before the download is safe: PZ downloads <c>WorkshopItems=</c> before loading <c>Mods=</c>, and
/// skips an id it can't find with a warning (spike #291, PZ 42.21).
/// </summary>
/// <param name="WorkshopId">The Workshop item id.</param>
/// <param name="CandidateModIds">The ids the description lists, in first-seen order.</param>
/// <param name="Kind">None, one, or several.</param>
public sealed record ModInstallPlan(string WorkshopId, IReadOnlyList<PzModId> CandidateModIds, ModInstallKind Kind)
{
    /// <summary>Plans the Install of <paramref name="item"/> from its description.</summary>
    public static ModInstallPlan For(WorkshopItemMetadata item)
    {
        ArgumentNullException.ThrowIfNull(item);

        IReadOnlyList<PzModId> ids = item.Found ? WorkshopDescriptionModIds.Parse(item.Description) : [];
        ModInstallKind kind = ids.Count switch
        {
            0 => ModInstallKind.NoIds,
            1 => ModInstallKind.OneId,
            _ => ModInstallKind.Choose,
        };
        return new ModInstallPlan(item.WorkshopId, ids, kind);
    }
}
