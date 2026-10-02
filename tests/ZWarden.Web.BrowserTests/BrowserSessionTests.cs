namespace ZWarden.Web.BrowserTests;

/// <summary>The smoke tier is only as good as its error recorder, so it is tested directly: a console error and an
/// uncaught exception on the page are both caught.</summary>
[ClassDataSource<BrowserHost>(Shared = SharedType.PerTestSession)]
public sealed class BrowserSessionTests(BrowserHost host)
{
    [Test]
    public async Task A_console_error_and_an_uncaught_exception_are_both_recorded()
    {
        await using BrowserSession session = await host.OpenAsync("/servers");

        await session.Page.EvaluateAsync("() => console.error('smoke-probe')");
        await session.Page.EvaluateAsync("() => setTimeout(() => { throw new Error('smoke-throw'); }, 0)");
        await session.Page.WaitForTimeoutAsync(200);

        await Assert.That(session.Errors).Contains(e => e.Contains("smoke-probe", StringComparison.Ordinal));
        await Assert.That(session.Errors).Contains(e => e.Contains("smoke-throw", StringComparison.Ordinal));
    }
}
