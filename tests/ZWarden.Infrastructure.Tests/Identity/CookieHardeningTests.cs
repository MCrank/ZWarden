using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HostFiltering;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ZWarden.Infrastructure.Identity;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;

namespace ZWarden.Infrastructure.Tests.Identity;

/// <summary>
/// F4 S3: the application session cookie is hardened (HttpOnly + always-Secure + SameSite + bounded
/// sliding expiration) and host-header validation is configured (PRD 11, trust-boundaries §2, ADR 0006).
/// Offline tier - configuration is asserted off the composed options, no server needed.
/// </summary>
public class CookieHardeningTests
{
    [Test]
    public async Task The_application_cookie_is_http_only_secure_same_site_and_bounded()
    {
        using ServiceProvider sp = Compose(["zwarden.example"]);
        CookieAuthenticationOptions cookie = sp
            .GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>()
            .Get(IdentityConstants.ApplicationScheme);

        await Assert.That(cookie.Cookie.Name).IsEqualTo("zwarden.auth");
        await Assert.That(cookie.Cookie.HttpOnly).IsTrue();
        await Assert.That(cookie.Cookie.SecurePolicy).IsEqualTo(CookieSecurePolicy.Always);
        await Assert.That(cookie.Cookie.SameSite).IsEqualTo(SameSiteMode.Lax);
        await Assert.That(cookie.SlidingExpiration).IsTrue();
        await Assert.That(cookie.ExpireTimeSpan).IsEqualTo(TimeSpan.FromHours(8));
    }

    [Test]
    public async Task Host_header_validation_is_configured_to_an_explicit_allow_list()
    {
        using ServiceProvider sp = Compose(["zwarden.example", "localhost"]);
        HostFilteringOptions options = sp.GetRequiredService<IOptions<HostFilteringOptions>>().Value;

        await Assert.That(options.AllowedHosts).Contains("zwarden.example");
        await Assert.That(options.AllowEmptyHosts).IsFalse();
        await Assert.That(options.IncludeFailureMessage).IsFalse();
    }

    [Test]
    public async Task A_wildcard_or_empty_allowed_host_is_rejected()
    {
        await Assert.That(() => Compose(["*"])).Throws<ArgumentException>();
        await Assert.That(() => Compose([])).Throws<ArgumentException>();
    }

    private static ServiceProvider Compose(string[] allowedHosts)
    {
        ServiceCollection services = new();
        services.AddLogging();
        services.AddTenantFoundation();
        services.AddZWardenPersistence(ZWardenDbProvider.Sqlite, "Data Source=:memory:");
        services.AddZWardenAuthentication(allowedHosts);
        return services.BuildServiceProvider();
    }
}
