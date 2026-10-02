using Microsoft.Playwright;

namespace ZWarden.Web.BrowserTests;

/// <summary>
/// One page in its own browser context, recording every console error and uncaught page error (#299, ADR 0046 Q4).
/// A circuit failure (an unhandled exception in an event handler, a render that throws) surfaces only here: Blazor
/// logs it to the console, and bUnit and the real-host page tests never see it.
/// </summary>
public sealed class BrowserSession : IAsyncDisposable
{
    private readonly IBrowserContext _context;
    private readonly List<string> _errors = [];

    internal BrowserSession(IBrowserContext context, IPage page)
    {
        _context = context;
        Page = page;
        page.Console += (_, message) =>
        {
            if (message.Type == "error")
            {
                lock (_errors)
                {
                    _errors.Add($"console: {message.Text}");
                }
            }
        };
        page.PageError += (_, error) =>
        {
            lock (_errors)
            {
                _errors.Add($"page error: {error}");
            }
        };
    }

    public IPage Page { get; }

    /// <summary>Every error seen so far.</summary>
    public IReadOnlyList<string> Errors
    {
        get
        {
            lock (_errors)
            {
                return [.. _errors];
            }
        }
    }

    internal async Task GotoAsync(string relativeUrl)
    {
        IResponse? response = await Page.GotoAsync(relativeUrl);
        if (response is null || !response.Ok)
        {
            throw new InvalidOperationException($"GET {relativeUrl} answered {response?.Status.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nothing"}.");
        }
    }

    /// <summary>Fails the test if the browser reported any error.</summary>
    public async Task AssertNoErrorsAsync()
    {
        IReadOnlyList<string> errors = Errors;
        await Assert.That(errors).IsEmpty()
            .Because($"the browser reported: {string.Join(" | ", errors)}");
    }

    public async ValueTask DisposeAsync() => await _context.DisposeAsync();
}
