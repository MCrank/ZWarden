using ZWarden.Application.Diagnostics;

namespace ZWarden.Diagnostics.Tests;

/// <summary>
/// F29 PR-A: the report's headline rollup and the untrusted-detail bound. <see cref="DiagnosticReport.Worst"/>
/// treats Skipped as a neutral floor and orders Pass &lt; Warn &lt; Fail; <see cref="DiagnosticCheck.Create"/>
/// truncates untrusted detail.
/// </summary>
public class DiagnosticReportTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    private static DiagnosticCheck Check(DiagnosticStatus status) =>
        new(DiagnosticDomain.Web, status, "s");

    [Test]
    public async Task An_empty_report_is_skipped()
    {
        DiagnosticReport report = new([], Now);

        await Assert.That(report.Worst()).IsEqualTo(DiagnosticStatus.Skipped);
    }

    [Test]
    public async Task A_fail_outranks_a_warn_and_a_pass()
    {
        DiagnosticReport report = new(
            [Check(DiagnosticStatus.Pass), Check(DiagnosticStatus.Fail), Check(DiagnosticStatus.Warn)], Now);

        await Assert.That(report.Worst()).IsEqualTo(DiagnosticStatus.Fail);
    }

    [Test]
    public async Task A_warn_outranks_a_pass()
    {
        DiagnosticReport report = new([Check(DiagnosticStatus.Pass), Check(DiagnosticStatus.Warn)], Now);

        await Assert.That(report.Worst()).IsEqualTo(DiagnosticStatus.Warn);
    }

    [Test]
    public async Task A_pass_outranks_a_skipped()
    {
        DiagnosticReport report = new([Check(DiagnosticStatus.Skipped), Check(DiagnosticStatus.Pass)], Now);

        await Assert.That(report.Worst()).IsEqualTo(DiagnosticStatus.Pass);
    }

    [Test]
    public async Task An_all_skipped_report_is_skipped()
    {
        DiagnosticReport report = new([Check(DiagnosticStatus.Skipped), Check(DiagnosticStatus.Skipped)], Now);

        await Assert.That(report.Worst()).IsEqualTo(DiagnosticStatus.Skipped);
    }

    [Test]
    public async Task Create_truncates_untrusted_detail_to_the_bound()
    {
        string hostile = new('x', DiagnosticCheck.MaxDetailLength + 100);

        DiagnosticCheck check = DiagnosticCheck.Create(DiagnosticDomain.Mod, DiagnosticStatus.Warn, "s", hostile);

        await Assert.That(check.Detail!.Length).IsEqualTo(DiagnosticCheck.MaxDetailLength);
    }

    [Test]
    public async Task Create_leaves_short_detail_untouched()
    {
        DiagnosticCheck check = DiagnosticCheck.Create(DiagnosticDomain.Mod, DiagnosticStatus.Warn, "s", "short");

        await Assert.That(check.Detail).IsEqualTo("short");
    }
}
