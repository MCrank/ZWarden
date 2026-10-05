using BlazorBlueprint.Primitives;
using Bunit;
using Bunit.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ZWarden.Application.Agents;
using ZWarden.Domain.Agents;
using ZWarden.Domain.Enrollments;
using ZWarden.Infrastructure.Persistence;
using ZWarden.Infrastructure.Tenancy;
using ZWarden.Web.Components.Hosts;

namespace ZWarden.Web.Tests.Hosts;

/// <summary>
/// #342: the Hosts page's "Enroll host" sheet, which replaces the /enrollment page. One step: an optional label and
/// Generate token, then the one-time token with copy, expiry and the Agent's .env lines, the tenant's recent tokens,
/// and "connected ✓" once the new host's Agent is live. Issues through the real <c>IEnrollmentService</c>.
/// </summary>
public sealed class EnrollHostSheetTests
{
    private const string AgentHash = "agenthashaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Test]
    public async Task The_button_opens_the_sheet_with_the_label_field_and_generate()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<ContainerFragment> cut = RenderWithPortal(harness);
        await Assert.That(cut.FindAll("[data-enroll-host-sheet]")).IsEmpty();

        await cut.Find("[data-action=enroll-host-open]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-enroll-host-sheet]").Count == 1);
        await Assert.That(cut.FindAll("#enroll-label")).Count().IsEqualTo(1);
        await Assert.That(cut.Find("[data-action=step-finish]").TextContent.Trim()).IsEqualTo("Generate token");
        await Assert.That(cut.FindAll("[data-step-strip]")).IsEmpty(); // one step, no strip
        await Assert.That(cut.FindAll("[data-enrollment-secret]")).IsEmpty();
    }

    [Test]
    public async Task Open_on_load_opens_the_sheet_for_the_enroll_deep_link()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();

        IRenderedComponent<ContainerFragment> cut = RenderWithPortal(harness, openOnLoad: true);

        cut.WaitForState(() => cut.FindAll("[data-enroll-host-sheet]").Count == 1);
    }

    [Test]
    public async Task Generate_shows_the_one_time_token_with_copy_expiry_and_the_env_lines()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<ContainerFragment> cut = await OpenAsync(harness);
        await InteractivePageHarness.TypeAsync(cut, "enroll-label", "host-alpha");

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-enrollment-secret]").Count == 1);
        string token = cut.Find("[data-enrollment-token]").TextContent.Trim();
        await Assert.That(token).StartsWith("zwe_");
        await Assert.That(cut.Find("[data-shell-copy='[data-enrollment-token]']")).IsNotNull();
        await Assert.That(cut.Find("[data-enrollment-expiry]").TextContent).Contains("UTC");
        string env = cut.Find("[data-enrollment-env]").TextContent;
        await Assert.That(env).Contains($"ZWARDEN_ENROLLMENT_SECRET={token}");
        await Assert.That(env).Contains("ZWARDEN_DOMAIN=localhost");
        await Assert.That(cut.Find("[data-shell-copy='[data-enrollment-env]']")).IsNotNull();
        await Assert.That(cut.Find("[data-enrollment-waiting]").TextContent).Contains("Waiting for the Agent");
        // The token is listed under Recent tokens with its label, and the finish button now just closes the sheet.
        await Assert.That(cut.Find("[data-enrollment-row]").TextContent).Contains("host-alpha");
        await Assert.That(cut.Find("[data-action=step-finish]").TextContent.Trim()).IsEqualTo("Done");
        await Assert.That(cut.FindAll("#enroll-label")).IsEmpty();
    }

    [Test]
    public async Task The_issued_token_is_saved_with_its_label()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<ContainerFragment> cut = await OpenAsync(harness);
        await InteractivePageHarness.TypeAsync(cut, "enroll-label", "  host-bravo  ");

        await cut.Find("[data-action=step-finish]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-enrollment-secret]").Count == 1);

        Enrollment saved = await LatestEnrollmentAsync(harness);
        await Assert.That(saved.Label).IsEqualTo("host-bravo");
        await Assert.That(saved.Status).IsEqualTo(EnrollmentStatus.Pending);
    }

    [Test]
    public async Task Done_closes_the_sheet_and_a_reopen_starts_fresh()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<ContainerFragment> cut = await OpenAsync(harness);
        await cut.Find("[data-action=step-finish]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-enrollment-secret]").Count == 1);

        await cut.Find("[data-action=step-finish]").ClickAsync(new());

        cut.WaitForState(() => cut.FindAll("[data-enroll-host-sheet]").Count == 0);
        await cut.Find("[data-action=enroll-host-open]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-enroll-host-sheet]").Count == 1);
        await Assert.That(cut.FindAll("[data-enrollment-secret]")).IsEmpty(); // the secret is shown once
        await Assert.That(cut.FindAll("[data-enrollment-row]").Count).IsEqualTo(1);
    }

    [Test]
    public async Task The_sheet_says_when_the_new_host_connects()
    {
        await using InteractivePageHarness harness = await InteractivePageHarness.StartAsync();
        IRenderedComponent<ContainerFragment> cut = await OpenAsync(harness);
        await cut.Find("[data-action=step-finish]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-enrollment-secret]").Count == 1);

        await RedeemLatestAsync(harness, "host-charlie");

        cut.WaitForState(() => cut.FindAll("[data-enrollment-connected]").Count == 1, TimeSpan.FromSeconds(15));
        await Assert.That(cut.Find("[data-enrollment-connected]").TextContent).Contains("host-charlie connected");
        await Assert.That(cut.FindAll("[data-enrollment-waiting]")).IsEmpty();
        // Live pass: Recent tokens no longer says the used token is Pending.
        await Assert.That(cut.Find("[data-enrollment-row]").TextContent).Contains("Consumed");
    }

    // The sheet renders through the Hosts page's single BbPortalHost (#363), so the test renders one beside it.
    private static IRenderedComponent<ContainerFragment> RenderWithPortal(InteractivePageHarness harness, bool openOnLoad = false)
        => harness.Context.Render(builder =>
        {
            builder.OpenComponent<EnrollHostSheet>(0);
            builder.AddComponentParameter(1, nameof(EnrollHostSheet.OpenOnLoad), openOnLoad);
            builder.CloseComponent();
            builder.OpenComponent<BbPortalHost>(2);
            builder.CloseComponent();
        });

    private static async Task<IRenderedComponent<ContainerFragment>> OpenAsync(InteractivePageHarness harness)
    {
        IRenderedComponent<ContainerFragment> cut = RenderWithPortal(harness);
        await cut.Find("[data-action=enroll-host-open]").ClickAsync(new());
        cut.WaitForState(() => cut.FindAll("[data-enroll-host-sheet]").Count == 1);
        return cut;
    }

    private static async Task<Enrollment> LatestEnrollmentAsync(InteractivePageHarness harness)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        return (await db.Set<Enrollment>().ToListAsync()).OrderByDescending(e => e.Id.ToString()).First();
    }

    // What the Agent's enrollment exchange does: consume the token, create the Agent, and connect.
    private static async Task RedeemLatestAsync(InteractivePageHarness harness, string label)
    {
        await using AsyncServiceScope scope = harness.Factory.Services.CreateSystemScope();
        ZWardenDbContext db = scope.ServiceProvider.GetRequiredService<ZWardenDbContext>();
        Enrollment enrollment = (await db.Set<Enrollment>().ToListAsync()).OrderByDescending(e => e.Id.ToString()).First();
        Agent agent = Agent.Enroll(AgentHash, enrollment.Id, DateTimeOffset.UtcNow, label);
        enrollment.Consume(agent.Id, DateTimeOffset.UtcNow);
        db.Set<Agent>().Add(agent);
        await db.SaveChangesAsync();
        harness.Factory.Services.GetRequiredService<IAgentConnectionRegistry>().Register(agent.Id, Guid.NewGuid().ToString(), () => { });
    }
}
