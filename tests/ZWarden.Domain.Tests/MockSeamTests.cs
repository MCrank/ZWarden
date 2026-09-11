using TUnit.Mocks;

namespace ZWarden.Domain.Tests;

/// <summary>
/// Proves the TUnit.Mocks seam compiles and runs (ADR 0002). Only a single shape
/// is exercised here - the seam is free, so it is revisited if a real mocking need
/// exposes a gap. A test-local interface stands in for a domain abstraction, which
/// does not exist until Feature 1.
/// </summary>
public class MockSeamTests
{
    public interface IClock
    {
        int UtcYear();
    }

    [Test]
    public async Task Mock_returns_the_configured_value()
    {
        var clock = IClock.Mock();
        clock.UtcYear().Returns(2026);

        await Assert.That(clock.Object.UtcYear()).IsEqualTo(2026);
    }
}
