using ZWarden.Domain.Servers;

namespace ZWarden.Domain.Tests.Servers;

/// <summary>
/// #114: a graceful-restart countdown schedule is a strictly-descending list of positive lead-times, bounded in
/// step count and reach so it can never pin the per-server lock (ADR 0022). An empty schedule is the valid "skip
/// the broadcast". The optional reason carries the same printable-ASCII hygiene as a broadcast message.
/// </summary>
public class GracefulRestartRulesTests
{
    [Test]
    public async Task The_default_shaped_schedule_is_accepted()
    {
        await Assert.That(GracefulRestartRules.ValidateSchedule([300, 60, 30, 10])).IsNull();
    }

    [Test]
    public async Task An_empty_schedule_is_accepted_as_skip()
    {
        await Assert.That(GracefulRestartRules.ValidateSchedule([])).IsNull();
        await Assert.That(GracefulRestartRules.ValidateSchedule(null)).IsNull();
    }

    [Test]
    public async Task A_single_step_schedule_is_accepted()
    {
        await Assert.That(GracefulRestartRules.ValidateSchedule([60])).IsNull();
    }

    [Test]
    [Arguments(new[] { 10, 30, 60 })]   // ascending
    [Arguments(new[] { 60, 60 })]       // not strictly descending (duplicate)
    [Arguments(new[] { 60, 30, 30 })]   // duplicate later
    public async Task A_non_descending_schedule_is_rejected(int[] schedule)
    {
        await Assert.That(GracefulRestartRules.ValidateSchedule(schedule)).IsNotNull();
    }

    [Test]
    [Arguments(new[] { 60, 0 })]        // zero lead-time
    [Arguments(new[] { 60, -5 })]       // negative lead-time
    public async Task A_non_positive_lead_time_is_rejected(int[] schedule)
    {
        await Assert.That(GracefulRestartRules.ValidateSchedule(schedule)).IsNotNull();
    }

    [Test]
    public async Task A_schedule_that_starts_too_early_is_rejected()
    {
        await Assert.That(GracefulRestartRules.ValidateSchedule([GracefulRestartRules.MaxCountdownSeconds + 1, 10]))
            .IsNotNull();
    }

    [Test]
    public async Task A_schedule_at_the_reach_bound_is_accepted()
    {
        await Assert.That(GracefulRestartRules.ValidateSchedule([GracefulRestartRules.MaxCountdownSeconds, 10]))
            .IsNull();
    }

    [Test]
    public async Task A_schedule_with_too_many_steps_is_rejected()
    {
        int[] tooMany = [.. Enumerable.Range(1, GracefulRestartRules.MaxCountdownSteps + 1)
            .Select(i => (GracefulRestartRules.MaxCountdownSteps + 2 - i))];
        await Assert.That(GracefulRestartRules.ValidateSchedule(tooMany)).IsNotNull();
    }

    [Test]
    [Arguments(null)]
    [Arguments("Scheduled maintenance.")]
    [Arguments("Applying mod changes")]
    public async Task A_clean_or_absent_reason_is_accepted(string? reason)
    {
        await Assert.That(GracefulRestartRules.ValidateReason(reason)).IsNull();
    }

    [Test]
    [Arguments("said \"hi\"")]
    [Arguments("line\nbreak")]
    [Arguments("café")]
    public async Task An_injection_reason_is_rejected(string reason)
    {
        await Assert.That(GracefulRestartRules.ValidateReason(reason)).IsNotNull();
    }

    [Test]
    public async Task An_over_long_reason_is_rejected()
    {
        string tooLong = new('a', GracefulRestartRules.MaxReasonLength + 1);
        await Assert.That(GracefulRestartRules.ValidateReason(tooLong)).IsNotNull();
    }
}
