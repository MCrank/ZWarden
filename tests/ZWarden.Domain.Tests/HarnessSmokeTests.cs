namespace ZWarden.Domain.Tests;

/// <summary>
/// Proves the tier-1 harness itself: TUnit discovers and runs a test, and the
/// Method_State_Expectation naming convention is legal here (CA1707 carve-out,
/// ADR 0013). Real domain tests arrive with Feature 1.
/// </summary>
public class HarnessSmokeTests
{
    [Test]
    public async Task Tier1_harness_runs_a_passing_test()
    {
        int[] values = [1, 1];
        await Assert.That(values.Sum()).IsEqualTo(2);
    }

    [Test]
    public async Task Underscore_test_names_are_permitted_in_test_assemblies()
    {
        // The mere existence of this method compiling under warnings-as-errors
        // is the real assertion: CA1707 would make this name a build error
        // anywhere outside tests/**.
        string ns = typeof(HarnessSmokeTests).Namespace!;
        await Assert.That(ns).StartsWith("ZWarden");
    }
}
