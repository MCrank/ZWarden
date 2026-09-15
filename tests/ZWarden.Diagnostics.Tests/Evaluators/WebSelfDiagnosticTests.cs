using ZWarden.Application.Diagnostics;
using ZWarden.Diagnostics.Evaluators;

namespace ZWarden.Diagnostics.Tests.Evaluators;

/// <summary>
/// F29 PR-A: the base health check. Reaching the evaluator is proof the Web host is up, so it always passes; the
/// build version rides along as detail.
/// </summary>
public class WebSelfDiagnosticTests
{
    [Test]
    public async Task The_web_self_check_passes_with_a_version()
    {
        DiagnosticCheck check = WebSelfDiagnostic.Evaluate("1.2.3");

        await Assert.That(check.Domain).IsEqualTo(DiagnosticDomain.Web);
        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Pass);
        await Assert.That(check.Detail).Contains("1.2.3");
    }

    [Test]
    public async Task The_web_self_check_passes_without_a_version()
    {
        DiagnosticCheck check = WebSelfDiagnostic.Evaluate(null);

        await Assert.That(check.Status).IsEqualTo(DiagnosticStatus.Pass);
        await Assert.That(check.Detail).IsNull();
    }
}
