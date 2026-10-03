using ZWarden.Application.Mods;

namespace ZWarden.Web.Components.Pages.Servers.Sections;

/// <summary>What a Mods-table row asks for (#292).</summary>
public enum ModRowVerb
{
    /// <summary>Remove the item (or turn the other mod off).</summary>
    Remove,

    /// <summary>Take back the row's pending change.</summary>
    Undo,

    /// <summary>Load the row's mods earlier.</summary>
    MoveUp,

    /// <summary>Load the row's mods later.</summary>
    MoveDown,

    /// <summary>Turn on exactly <see cref="ModRowCommand.Parts"/>.</summary>
    SaveParts,
}

/// <summary>A row action raised by <c>ModsTable</c> for <c>ModsSection</c> to run.</summary>
/// <param name="Verb">The action.</param>
/// <param name="Row">The row it concerns.</param>
/// <param name="Parts">The ticked parts, for <see cref="ModRowVerb.SaveParts"/>; otherwise empty.</param>
public sealed record ModRowCommand(ModRowVerb Verb, ModTableRow Row, IReadOnlyList<string> Parts);
