using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Security;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Agents;

/// <summary>
/// DEV-ONLY (ADR 0031, #123): seeds a well-known, redeemable <see cref="Enrollment"/> so an
/// Aspire-orchestrated Agent self-enrolls on first boot with no manual enrollment step. It mirrors
/// <see cref="Identity.AdminBootstrapper"/> — idempotent, ambient-default-tenant, safe on every boot —
/// and is driven only when the host supplies the dev enrollment secret via
/// <c>ZWarden:Dev:EnrollmentSecret</c> (which the AppHost injects in Development).
/// <para>
/// It <b>fails closed outside Development</b>: if that secret is ever configured while the host is not
/// in the Development environment, it throws rather than seeding, so the well-known dev credential can
/// never become a production authentication path (D-ENROLL guard). Enrollment stores only a one-way
/// hash (no key ring), so a seeded credential carries no encryption-at-rest dependency.
/// </para>
/// </summary>
public static class DevEnrollmentBootstrapper
{
    /// <summary>The provenance label carried on the dev-seeded enrollment; never a secret.</summary>
    public const string DevEnrollmentLabel = "dev-aspire-bootstrap";

    /// <summary>
    /// Resolves persistence from <paramref name="services"/> in a fresh scope (so the seed lands under the
    /// ambient default tenant) and ensures the dev enrollment, when <paramref name="enrollmentSecret"/> is
    /// supplied. Call once at startup, after the tenant/admin bootstrap. See the type summary for the
    /// out-of-Development guard.
    /// </summary>
    public static async Task EnsureDevEnrollmentAsync(
        IServiceProvider services,
        bool isDevelopment,
        string? enrollmentSecret,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Cheap checks before touching the container: nothing to do without a secret; fail closed if it is
        // present outside Development (before any scope is created or the database is read).
        if (string.IsNullOrWhiteSpace(enrollmentSecret))
        {
            return;
        }

        Guard(isDevelopment);

        using IServiceScope scope = services.CreateScope();
        ZWardenDbContext context = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        ICredentialHasher hasher = scope.ServiceProvider.GetRequiredService<ICredentialHasher>();
        TimeProvider clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();

        await EnsureDevEnrollmentAsync(
            context, hasher, clock.GetUtcNow(), isDevelopment, enrollmentSecret, lifetime, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// The seeding core, over an already-resolved <paramref name="context"/> (its ambient tenant is where
    /// the enrollment lands). Ensures exactly one redeemable enrollment carries the well-known secret's
    /// hash: returns if one already exists, otherwise removes any stale (consumed/expired/revoked) rows
    /// with that hash — keeping the exchange's hash lookup deterministic — and mints a fresh one.
    /// </summary>
    public static async Task EnsureDevEnrollmentAsync(
        ZWardenDbContext context,
        ICredentialHasher hasher,
        DateTimeOffset now,
        bool isDevelopment,
        string? enrollmentSecret,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(hasher);

        if (string.IsNullOrWhiteSpace(enrollmentSecret))
        {
            return;
        }

        Guard(isDevelopment);

        if (lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Dev enrollment lifetime must be positive.");
        }

        string hash = hasher.Hash(new SecretString(enrollmentSecret));

        List<Enrollment> matching = await context.Set<Enrollment>()
            .Where(e => e.SecretHash == hash)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (matching.Exists(e => e.IsRedeemable(now)))
        {
            return;
        }

        if (matching.Count > 0)
        {
            context.RemoveRange(matching);
        }

        context.Add(Enrollment.Issue(hash, UserId.New(), now, now + lifetime, DevEnrollmentLabel));
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void Guard(bool isDevelopment)
    {
        if (!isDevelopment)
        {
            throw new InvalidOperationException(
                "A dev enrollment secret (ZWarden:Dev:EnrollmentSecret) is configured but the host " +
                "environment is not Development. The well-known dev enrollment credential must never " +
                "authenticate outside Development (ADR 0031).");
        }
    }
}
