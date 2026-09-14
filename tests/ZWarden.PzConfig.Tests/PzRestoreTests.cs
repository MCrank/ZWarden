using System.Text;
using ZWarden.PzConfig;
using ZWarden.PzConfig.Internal;
using ZWarden.PzConfig.Model;
using ZWarden.PzConfig.Revisions;

namespace ZWarden.PzConfig.Tests;

/// <summary>
/// Restoring a revision is a value-level re-apply, never a byte restore (ADR 0011): a plan of surgical
/// value edits that make the current file's values match a target snapshot, plus a report of the
/// structural differences (a key present in one and not the other) that the values-only writer cannot
/// apply. The plan is non-destructive; applying it uses the same <see cref="IPzConfigDocument.TrySetValue"/>.
/// </summary>
public class PzRestoreTests
{
    private static IPzConfigDocument Sandbox(string body) =>
        LuaConfigReader.Read(PzConfigKind.SandboxVars, Encoding.UTF8.GetBytes(body)).Document!;

    [Test]
    public async Task Restoring_to_an_identical_document_yields_an_empty_plan()
    {
        PzValueSnapshot target = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n}"));
        IPzConfigDocument current = Sandbox("SandboxVars = {\n    Zombies = 4,\n}");

        PzRestorePlan plan = PzRestore.PlanTo(target, current);

        await Assert.That(plan.Edits).IsEmpty();
        await Assert.That(plan.Obstacles).IsEmpty();
        await Assert.That(plan.IsClean).IsTrue();
    }

    [Test]
    public async Task A_changed_value_becomes_a_single_edit()
    {
        PzValueSnapshot target = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n    Speed = 2,\n}"));
        IPzConfigDocument current = Sandbox("SandboxVars = {\n    Zombies = 1,\n    Speed = 2,\n}");

        PzRestorePlan plan = PzRestore.PlanTo(target, current);

        await Assert.That(plan.Obstacles).IsEmpty();
        await Assert.That(plan.Edits.Count).IsEqualTo(1);
        await Assert.That(plan.Edits[0].Path).IsEqualTo("Zombies");
        await Assert.That(((PzNumber)plan.Edits[0].Value).Value).IsEqualTo(4d);
    }

    [Test]
    public async Task Applying_the_plan_makes_the_current_values_match_the_target()
    {
        PzValueSnapshot target = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n    Speed = 5,\n}"));
        IPzConfigDocument current = Sandbox("SandboxVars = {\n    Zombies = 1,\n    Speed = 2,\n}");

        PzRestorePlan plan = PzRestore.PlanTo(target, current);
        IReadOnlyList<PzConfigEditResult> results = PzRestore.Apply(current, plan);

        await Assert.That(results.All(r => r.Ok)).IsTrue();
        await Assert.That(((PzNumber)Read(current, "Zombies")).Value).IsEqualTo(4d);
        await Assert.That(((PzNumber)Read(current, "Speed")).Value).IsEqualTo(5d);
    }

    [Test]
    public async Task A_key_in_the_target_but_not_the_file_is_an_unrestorable_addition()
    {
        PzValueSnapshot target = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n    Rain = 3,\n}"));
        IPzConfigDocument current = Sandbox("SandboxVars = {\n    Zombies = 4,\n}");

        PzRestorePlan plan = PzRestore.PlanTo(target, current);

        await Assert.That(plan.Edits).IsEmpty();
        await Assert.That(plan.Obstacles.Count).IsEqualTo(1);
        await Assert.That(plan.Obstacles[0].Path).IsEqualTo("Rain");
        await Assert.That(plan.Obstacles[0].Kind).IsEqualTo(PzRestoreObstacleKind.AddNotSupported);
        await Assert.That(plan.IsClean).IsFalse();
    }

    [Test]
    public async Task A_key_in_the_file_but_not_the_target_is_an_unrestorable_removal()
    {
        PzValueSnapshot target = PzValueSnapshot.Of(Sandbox("SandboxVars = {\n    Zombies = 4,\n}"));
        IPzConfigDocument current = Sandbox("SandboxVars = {\n    Zombies = 4,\n    Fog = 5,\n}");

        PzRestorePlan plan = PzRestore.PlanTo(target, current);

        await Assert.That(plan.Edits).IsEmpty();
        await Assert.That(plan.Obstacles.Count).IsEqualTo(1);
        await Assert.That(plan.Obstacles[0].Path).IsEqualTo("Fog");
        await Assert.That(plan.Obstacles[0].Kind).IsEqualTo(PzRestoreObstacleKind.RemoveNotSupported);
    }

    private static PzValue Read(IPzConfigDocument doc, string path)
    {
        doc.TryGetValue(path, out PzValue value);
        return value;
    }
}
