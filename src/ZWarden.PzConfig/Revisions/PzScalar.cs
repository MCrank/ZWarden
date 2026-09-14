using ZWarden.PzConfig.Model;

namespace ZWarden.PzConfig.Revisions;

/// <summary>
/// Value equality for scalar leaves, shared by the diff, snapshot and restore (ADR 0011 is value-level
/// throughout, so they must agree). Numbers compare by magnitude <em>and</em> integer-ness (so <c>1</c>
/// and <c>1.0</c> differ but <c>1.0</c> and <c>1.00</c> do not), strings by ordinal, booleans by value.
/// </summary>
internal static class PzScalar
{
    public static bool AreEqual(PzValue a, PzValue b) => (a, b) switch
    {
        (PzBoolean ba, PzBoolean bb) => ba.Value == bb.Value,
        (PzString sa, PzString sb) => string.Equals(sa.Value, sb.Value, StringComparison.Ordinal),
        (PzNumber na, PzNumber nb) => na.Value.Equals(nb.Value) && na.IsInteger == nb.IsInteger,
        _ => false,
    };
}
