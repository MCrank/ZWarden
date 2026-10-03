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

    [Test]
    public async Task A_deliberate_disconnect_tolerates_only_the_send_on_a_closed_connection_error()
    {
        // #315: pausing the circuit can catch an in-flight render ack or interop reply, which the SignalR client
        // reports as an uncaught "Cannot send data…" error. Inside a deliberate-disconnect scope that one message
        // is expected; every other error is still recorded.
        await using BrowserSession session = await host.OpenAsync("/servers");

        using (session.ExpectDisconnect())
        {
            await Throw(session, BrowserSession.SendOnClosedConnectionError);
            await Throw(session, "smoke-other-error");
        }

        await Assert.That(session.Errors).DoesNotContain(e => e.Contains("Cannot send data", StringComparison.Ordinal));
        await Assert.That(session.Errors).Contains(e => e.Contains("smoke-other-error", StringComparison.Ordinal));
    }

    [Test]
    public async Task Outside_a_deliberate_disconnect_the_send_on_a_closed_connection_error_is_recorded()
    {
        await using BrowserSession session = await host.OpenAsync("/servers");

        await Throw(session, BrowserSession.SendOnClosedConnectionError);

        await Assert.That(session.Errors).Contains(e => e.Contains("Cannot send data", StringComparison.Ordinal));
    }

    private static async Task Throw(BrowserSession session, string message)
    {
        await session.Page.EvaluateAsync("m => setTimeout(() => { throw new Error(m); }, 0)", message);
        await session.Page.WaitForTimeoutAsync(200);
    }
}
