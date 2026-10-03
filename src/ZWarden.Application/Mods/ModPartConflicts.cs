namespace ZWarden.Application.Mods;

/// <summary>
/// Which installed mods declare each other incompatible (<c>incompatible=</c> in <c>mod.info</c>), for the parts picker
/// (#292 live pass): a pack like Equipment UI ships two builds of one mod that must not load together. A declaration
/// counts both ways. Built from the last inventory; with none, nothing is known to conflict. Ordinal comparison.
/// </summary>
public sealed class ModPartConflicts
{
    private readonly Dictionary<string, HashSet<string>> _conflicts;

    private ModPartConflicts(Dictionary<string, HashSet<string>> conflicts) => _conflicts = conflicts;

    /// <summary>The conflicts the inventory's <c>mod.info</c> files declare.</summary>
    public static ModPartConflicts From(ModInventory? inventory)
    {
        Dictionary<string, HashSet<string>> conflicts = new(StringComparer.Ordinal);
        foreach (InstalledMod mod in inventory?.InstalledItems.SelectMany(i => i.Mods) ?? [])
        {
            foreach (string other in mod.Incompatible.Where(o => !string.Equals(o, mod.ModId, StringComparison.Ordinal)))
            {
                Add(mod.ModId, other);
                Add(other, mod.ModId);
            }
        }

        return new ModPartConflicts(conflicts);

        void Add(string from, string to)
        {
            if (!conflicts.TryGetValue(from, out HashSet<string>? set))
            {
                conflicts[from] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            set.Add(to);
        }
    }

    /// <summary>Every mod <paramref name="modId"/> conflicts with.</summary>
    public IReadOnlyCollection<string> Of(string modId) =>
        _conflicts.TryGetValue(modId, out HashSet<string>? set) ? set : [];

    /// <summary>The members of <paramref name="chosen"/> (other than itself) that <paramref name="modId"/> conflicts
    /// with, in <paramref name="chosen"/>'s order.</summary>
    public IReadOnlyList<string> With(string modId, IEnumerable<string> chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        IReadOnlyCollection<string> of = Of(modId);
        return [.. chosen.Where(c => !string.Equals(c, modId, StringComparison.Ordinal) && of.Contains(c))];
    }

    /// <summary>Two members of <paramref name="chosen"/> conflict.</summary>
    public bool AnyAmong(IReadOnlyCollection<string> chosen)
    {
        ArgumentNullException.ThrowIfNull(chosen);
        return chosen.Any(c => With(c, chosen).Count > 0);
    }
}
