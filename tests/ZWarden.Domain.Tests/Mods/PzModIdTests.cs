using ZWarden.Domain.Mods;

namespace ZWarden.Domain.Tests.Mods;

/// <summary>
/// #290 D3: the one gate every mod id passes before it can reach <c>Mods=</c> — a description guess, a
/// <c>mod.info</c> id, or an operator-typed id. Printable text is allowed (real ids carry spaces, apostrophes,
/// dots and dashes); config separators, path/escape characters, quotes and control characters are not, so an
/// untrusted id can never split or corrupt the semicolon-joined list (trust-boundaries §8).
/// </summary>
public class PzModIdTests
{
    [Test]
    [Arguments("ToadTraits")]
    [Arguments("Brita's Weapon Pack")]
    [Arguments("UCWF-core.v2")]
    [Arguments("mod_42")]
    [Arguments("ÆtherMod")]
    public async Task Accepts_printable_mod_ids(string candidate)
    {
        bool ok = PzModId.TryCreate(candidate, out PzModId id);

        await Assert.That(ok).IsTrue();
        await Assert.That(id.Value).IsEqualTo(candidate);
        await Assert.That(id.ToString()).IsEqualTo(candidate);
    }

    [Test]
    [Arguments("A;B")]
    [Arguments("A,B")]
    [Arguments("A=B")]
    [Arguments("A\\B")]
    [Arguments("A/B")]
    [Arguments("A\"B")]
    [Arguments("A\nB")]
    [Arguments("A\tB")]
    [Arguments("A\u0000B")]
    [Arguments(" Leading")]
    [Arguments("Trailing ")]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(null)]
    public async Task Rejects_separators_control_characters_and_blank_or_padded_ids(string? candidate)
    {
        bool ok = PzModId.TryCreate(candidate, out PzModId id);

        await Assert.That(ok).IsFalse();
        await Assert.That(id).IsEqualTo(default(PzModId));
    }

    [Test]
    public async Task Accepts_up_to_128_characters_and_rejects_longer()
    {
        await Assert.That(PzModId.TryCreate(new string('a', 128), out _)).IsTrue();
        await Assert.That(PzModId.TryCreate(new string('a', 129), out _)).IsFalse();
    }

    [Test]
    public async Task Compares_ordinally_so_case_variants_are_distinct()
    {
        PzModId.TryCreate("Mod", out PzModId upper);
        PzModId.TryCreate("mod", out PzModId lower);
        PzModId.TryCreate("Mod", out PzModId again);

        await Assert.That(upper).IsNotEqualTo(lower);
        await Assert.That(upper).IsEqualTo(again);
    }
}
