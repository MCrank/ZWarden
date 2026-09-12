using Microsoft.Extensions.Logging.Abstractions;
using ZWarden.Application.Audit;
using ZWarden.Application.Authentication;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Infrastructure.Audit;

namespace ZWarden.Infrastructure.Tests.Audit;

/// <summary>
/// F6 S5: the durable <see cref="AuditAuthenticationEventSink"/> maps each authentication-event kind to an
/// audit action/outcome and appends it through the writer, and — per the seam contract — never throws into
/// the auth path when the writer fails. Offline tier.
/// </summary>
public class AuditAuthenticationEventSinkTests
{
    [Test]
    [Arguments(AuthenticationEventKind.SignInSucceeded, "Authentication.SignInSucceeded", AuditOutcome.Succeeded)]
    [Arguments(AuthenticationEventKind.SignInFailed, "Authentication.SignInFailed", AuditOutcome.Failed)]
    [Arguments(AuthenticationEventKind.LockedOut, "Authentication.LockedOut", AuditOutcome.Failed)]
    [Arguments(AuthenticationEventKind.MfaVerified, "Authentication.MfaVerified", AuditOutcome.Succeeded)]
    [Arguments(AuthenticationEventKind.PasswordReset, "Authentication.PasswordReset", AuditOutcome.Succeeded)]
    [Arguments(AuthenticationEventKind.ExternalLoginLinked, "Authentication.ExternalLoginLinked", AuditOutcome.Succeeded)]
    public async Task Maps_each_kind_to_an_action_and_outcome_and_persists_it(
        AuthenticationEventKind kind, string expectedAction, AuditOutcome expectedOutcome)
    {
        SpyWriter writer = new();
        AuditAuthenticationEventSink sink = new(writer, NullLogger<AuditAuthenticationEventSink>.Instance);
        UserId user = UserId.New();

        await sink.RecordAsync(new AuthenticationEvent(kind, user, TenantId.New(), DateTimeOffset.UnixEpoch, "detail"));

        AuditEntry entry = writer.Entries.Single();
        await Assert.That(entry.Action).IsEqualTo(expectedAction);
        await Assert.That(entry.Outcome).IsEqualTo(expectedOutcome);
        await Assert.That(entry.ActorUserId).IsEqualTo(user);
        await Assert.That(entry.ServerId).IsNull();
        await Assert.That(entry.Detail).IsEqualTo("detail");
    }

    [Test]
    public async Task A_writer_failure_never_throws_into_the_auth_path()
    {
        AuditAuthenticationEventSink sink = new(new ThrowingWriter(), NullLogger<AuditAuthenticationEventSink>.Instance);

        // Must complete without throwing — a sink failure cannot block a sign-in.
        await sink.RecordAsync(new AuthenticationEvent(
            AuthenticationEventKind.SignInSucceeded, UserId.New(), TenantId.New(), DateTimeOffset.UnixEpoch));
    }

    private sealed class SpyWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("audit store unavailable");
    }
}
