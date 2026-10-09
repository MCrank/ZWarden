using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Settings;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Infrastructure.Tests.Agents;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// #346: the Owner's session idle timeout reaches the session cookie without a restart. A sign-in expires after the
/// tenant's timeout; a live ticket with another span is ended when it has been idle longer than the new timeout, or
/// re-spanned and renewed when it hasn't; a ticket already on the timeout is left alone.
/// </summary>
public sealed class SessionTimeoutCookieEventsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = TenantId.New();

    [Test]
    public async Task A_sign_in_expires_after_the_tenants_timeout()
    {
        CookieSigningInContext context = SigningIn(TimeSpan.FromMinutes(30));

        await SessionTimeoutCookieEvents.SigningInAsync(context);

        await Assert.That(context.Properties.IssuedUtc).IsEqualTo(Now);
        await Assert.That(context.Properties.ExpiresUtc).IsEqualTo(Now + TimeSpan.FromMinutes(30));
    }

    [Test]
    public async Task Without_an_override_a_sign_in_gets_the_8_hour_default()
    {
        CookieSigningInContext context = SigningIn(timeout: null);

        await SessionTimeoutCookieEvents.SigningInAsync(context);

        await Assert.That(context.Properties.ExpiresUtc).IsEqualTo(Now + TimeSpan.FromHours(8));
    }

    [Test]
    public async Task A_session_idle_longer_than_a_new_shorter_timeout_is_ended()
    {
        RecordingAuthentication auth = new();
        CookieValidatePrincipalContext context = Validating(
            TimeSpan.FromMinutes(30), issuedAgo: TimeSpan.FromMinutes(45), span: TimeSpan.FromHours(8), auth);

        await SessionTimeoutCookieEvents.ApplyTimeoutAsync(context);

        await Assert.That(context.Principal).IsNull();
        await Assert.That(auth.SignedOut).IsEqualTo(IdentityConstants.ApplicationScheme);
    }

    [Test]
    public async Task A_recently_active_session_is_re_spanned_to_the_new_timeout_and_renewed()
    {
        CookieValidatePrincipalContext context = Validating(
            TimeSpan.FromMinutes(30), issuedAgo: TimeSpan.FromMinutes(10), span: TimeSpan.FromHours(8), new RecordingAuthentication());

        await SessionTimeoutCookieEvents.ApplyTimeoutAsync(context);

        await Assert.That(context.Principal).IsNotNull();
        await Assert.That(context.ShouldRenew).IsTrue();
        await Assert.That(context.Properties.ExpiresUtc - context.Properties.IssuedUtc).IsEqualTo(TimeSpan.FromMinutes(30));
    }

    [Test]
    public async Task A_longer_timeout_extends_a_live_session()
    {
        CookieValidatePrincipalContext context = Validating(
            TimeSpan.FromDays(7), issuedAgo: TimeSpan.FromHours(7), span: TimeSpan.FromHours(8), new RecordingAuthentication());

        await SessionTimeoutCookieEvents.ApplyTimeoutAsync(context);

        await Assert.That(context.ShouldRenew).IsTrue();
        await Assert.That(context.Properties.ExpiresUtc - context.Properties.IssuedUtc).IsEqualTo(TimeSpan.FromDays(7));
    }

    [Test]
    public async Task A_ticket_already_on_the_timeout_is_left_alone()
    {
        CookieValidatePrincipalContext context = Validating(
            TimeSpan.FromMinutes(30), issuedAgo: TimeSpan.FromMinutes(20), span: TimeSpan.FromMinutes(30), new RecordingAuthentication());

        await SessionTimeoutCookieEvents.ApplyTimeoutAsync(context);

        await Assert.That(context.Principal).IsNotNull();
        await Assert.That(context.ShouldRenew).IsFalse();
    }

    private static CookieSigningInContext SigningIn(TimeSpan? timeout) =>
        new(Http(timeout, new RecordingAuthentication()), Scheme(), Options(), Principal(), new AuthenticationProperties(), new CookieOptions());

    private static CookieValidatePrincipalContext Validating(
        TimeSpan timeout, TimeSpan issuedAgo, TimeSpan span, RecordingAuthentication auth)
    {
        AuthenticationProperties properties = new() { IssuedUtc = Now - issuedAgo, ExpiresUtc = Now - issuedAgo + span };
        AuthenticationTicket ticket = new(Principal(), properties, IdentityConstants.ApplicationScheme);
        return new CookieValidatePrincipalContext(Http(timeout, auth), Scheme(), Options(), ticket);
    }

    private static DefaultHttpContext Http(TimeSpan? timeout, RecordingAuthentication auth)
    {
        ServiceCollection services = new();
        services.AddSingleton<IControlPlaneSettingsCache>(new FixedCache(new ControlPlaneSettingsSnapshot(null, timeout)));
        services.AddSingleton<IAuthenticationService>(auth);
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private static AuthenticationScheme Scheme() =>
        new(IdentityConstants.ApplicationScheme, null, typeof(CookieAuthenticationHandler));

    private static CookieAuthenticationOptions Options() => new() { TimeProvider = new StubClock(Now) };

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity([new Claim(ClaimsPrincipalTenantContext.TenantClaimType, Tenant.ToString())], "test"));

    private sealed class FixedCache(ControlPlaneSettingsSnapshot snapshot) : IControlPlaneSettingsCache
    {
        public Task<ControlPlaneSettingsSnapshot> GetAsync(TenantId tenant, CancellationToken cancellationToken = default) =>
            Task.FromResult(tenant == Tenant ? snapshot : ControlPlaneSettingsSnapshot.Empty);

        public void Remember(TenantId tenant, ControlPlaneSettingsSnapshot snapshot) { }
    }

    private sealed class RecordingAuthentication : IAuthenticationService
    {
        public string? SignedOut { get; private set; }

        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties)
        {
            SignedOut = scheme;
            return Task.CompletedTask;
        }

        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme) => throw new NotSupportedException();

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();

        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => throw new NotSupportedException();

        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) =>
            throw new NotSupportedException();
    }
}
