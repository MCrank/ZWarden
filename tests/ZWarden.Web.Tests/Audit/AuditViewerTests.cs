using System.Net;
using Bunit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Audit;
using ZWarden.Domain.Audit;
using ZWarden.Domain.Ids;
using ZWarden.Web.Components.Pages.Audit;
using ZWarden.Web.Tests.Account;

namespace ZWarden.Web.Tests.Audit;

/// <summary>
/// F6 S7: the administrative audit viewer is gated by the <c>Audit.View</c> permission server-side and
/// renders the tenant-scoped trail read through <see cref="IAuditQuery"/> (PRD 12 — enforcement, not UI
/// visibility). Offline tier.
/// </summary>
public class AuditViewerTests
{
    [Test]
    public async Task The_page_is_gated_by_the_audit_view_permission_server_side()
    {
        AuthorizeAttribute attribute = typeof(AuditLog)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .Cast<AuthorizeAttribute>()
            .Single();

        await Assert.That(attribute.Policy).IsEqualTo("Audit.View");
    }

    [Test]
    public async Task It_renders_a_row_per_audit_event_from_the_query()
    {
        using BunitContext ctx = new();
        ctx.Services.AddSingleton<IAuditQuery>(new StubQuery(
        [
            new AuditEventView(AuditEventId.New(), DateTimeOffset.UnixEpoch, "Authentication.SignInSucceeded",
                AuditOutcome.Succeeded, UserId.New(), null, "corr-1", "ok"),
            new AuditEventView(AuditEventId.New(), DateTimeOffset.UnixEpoch, "Role.Created",
                AuditOutcome.Succeeded, null, null, null, null),
        ]));

        IRenderedComponent<AuditLog> cut = ctx.Render<AuditLog>();

        await Assert.That(cut.FindAll("[data-audit-row]").Count).IsEqualTo(2);
        await Assert.That(cut.Markup).Contains("Authentication.SignInSucceeded");
        await Assert.That(cut.Find("[data-audit-total]").TextContent).Contains("2");
    }

    [Test]
    public async Task It_shows_an_empty_state_when_there_are_no_events()
    {
        using BunitContext ctx = new();
        ctx.Services.AddSingleton<IAuditQuery>(new StubQuery([]));

        IRenderedComponent<AuditLog> cut = ctx.Render<AuditLog>();

        await Assert.That(cut.FindAll("[data-audit-row]").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).Contains("No audit events match.");
    }

    [Test]
    public async Task Anonymous_request_to_audit_redirects_to_login()
    {
        await using ZWardenWebAppFactory factory = new();
        using HttpClient client = factory.CreateWebClient();

        HttpResponseMessage response = await client.GetAsync(new Uri("/audit", UriKind.Relative));

        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Redirect);
        Uri location = response.Headers.Location ?? throw new InvalidOperationException("No Location header.");
        string path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
        await Assert.That(path).StartsWith("/login");
    }

    private sealed class StubQuery : IAuditQuery
    {
        private readonly IReadOnlyList<AuditEventView> _events;
        public StubQuery(IReadOnlyList<AuditEventView> events) => _events = events;

        public Task<IReadOnlyList<AuditEventView>> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(_events);

        public Task<int> CountAsync(AuditQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(_events.Count);
    }
}
