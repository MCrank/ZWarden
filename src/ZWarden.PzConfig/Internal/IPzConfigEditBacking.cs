using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Internal;

/// <summary>
/// The edit backing a parsed document retains so it can replace a value surgically and re-emit the
/// file byte-for-byte (F20b, ADR 0010). It is <em>internal</em> by design: the Lua backing holds a
/// Loretta syntax tree, and keeping it off the public surface is what lets the seam stay
/// library-agnostic (the <c>ReferenceDirectionTests</c> guard). Each backing owns one file's source
/// and the current value model derived from it.
/// </summary>
internal interface IPzConfigEditBacking
{
    /// <summary>The current value model, reflecting any applied edits.</summary>
    PzTable Model { get; }

    /// <summary>Replaces the scalar at the dotted path in place; see <see cref="IPzConfigDocument.TrySetValue"/>.</summary>
    PzConfigEditResult TrySetValue(string path, PzValue newValue);

    /// <summary>The current source as BOM-less UTF-8 bytes.</summary>
    byte[] Emit();
}
