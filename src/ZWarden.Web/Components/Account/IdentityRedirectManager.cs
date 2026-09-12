using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;

namespace ZWarden.Web.Components.Account;

/// <summary>
/// Redirect helper for the static-rendered Account pages (F4 UI, issue #63). During static
/// server-side rendering, <see cref="NavigationManager.NavigateTo(string)"/> throws a
/// <c>NavigationException</c> that the framework turns into an HTTP 302 on the current response —
/// so these methods must only be called from a form handler running on the request, never from an
/// interactive circuit.
/// </summary>
/// <remarks>
/// Every caller-supplied <c>returnUrl</c> is forced <b>local</b> through <see cref="ToLocalOrHome"/>
/// (an absolute, protocol-relative, or back-slash URL is discarded), so a crafted return target can
/// never bounce the browser off-site. This is the open-redirect complement to host-header validation
/// (ADR 0006); the two together keep the browser boundary honest.
/// </remarks>
internal sealed class IdentityRedirectManager
{
    private readonly NavigationManager _navigationManager;

    public IdentityRedirectManager(NavigationManager navigationManager)
    {
        ArgumentNullException.ThrowIfNull(navigationManager);
        _navigationManager = navigationManager;
    }

    /// <summary>Returns <paramref name="returnUrl"/> only if it is a safe local path
    /// (starts with a single <c>/</c>, not <c>//</c> or <c>/\</c>); otherwise <c>"/"</c>.</summary>
    public static string ToLocalOrHome(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl)
            || returnUrl[0] != '/'
            || returnUrl.StartsWith("//", StringComparison.Ordinal)
            || returnUrl.StartsWith("/\\", StringComparison.Ordinal))
        {
            return "/";
        }

        return returnUrl;
    }

    /// <summary>Redirects to a relative path (as-is — callers pass literals they control).</summary>
    [DoesNotReturn]
    public void RedirectTo(string uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        _navigationManager.NavigateTo(uri);
        // NavigateTo throws NavigationException during static SSR; this guards a non-SSR misuse.
        throw new InvalidOperationException(
            $"{nameof(RedirectTo)} can only be used during static server-side rendering.");
    }

    /// <summary>Redirects to a caller-supplied return target, forced local first.</summary>
    [DoesNotReturn]
    public void RedirectToLocal(string? returnUrl) => RedirectTo(ToLocalOrHome(returnUrl));
}
