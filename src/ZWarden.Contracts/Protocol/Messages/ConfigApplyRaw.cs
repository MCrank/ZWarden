using ZWarden.Domain.Configuration;

namespace ZWarden.Contracts.Protocol.Messages;

/// <summary>
/// Apply an operator-authored <b>whole-file</b> edit to one of a Server's four configuration files (F20c PR-D,
/// ADR 0042). Unlike <see cref="ConfigApply"/>, which carries a handful of surgical value edits, a raw edit
/// replaces the entire file with text the operator typed — which can be ~45 KB, far over the 2 KB command-payload
/// cap. The text is therefore <b>staged</b> to the Agent over the <see cref="AgentHubProtocol.StageServerConfigRawEdit"/>
/// transport channel <b>before</b> this Operation is dispatched, and this command carries only a
/// <see cref="CorrelationId"/> the Agent uses to retrieve the staged text. Keeping the arbitrary text off the
/// command means no <see cref="AgentCommand"/> carries a free-form string — the closed-command vocabulary
/// (trust-boundaries.md §9 rule 3) holds by construction.
/// <para>
/// It is a <b>mutating, server-scoped</b> Operation, so it claims the per-server lock (ADR 0022). The Agent
/// parse-validates the staged text (a syntax error or size/depth violation is refused, never written), compares the
/// live file's canonical value hash against <see cref="BaselineHash"/> and <b>fails closed on a mismatch</b> (ADR
/// 0011), then writes the text back BOM-less through a temp-file-and-atomic-replace, reporting the same
/// <see cref="ConfigApplyResult"/> a surgical apply reports (so the revision is recorded). A missing or expired
/// staging entry fails the Operation with an actionable reason, never a hang.
/// </para>
/// </summary>
/// <param name="File">Which of the Server's four configuration files to overwrite.</param>
/// <param name="BaselineHash">The drift baseline the Agent re-checks before writing (the operator's live-read
/// baseline, ADR 0042), or <c>null</c> to skip the drift check for a first write with no baseline.</param>
/// <param name="CorrelationId">The id under which the operator's whole-file text was staged to the Agent — the
/// Agent retrieves the staged bytes by this id when it runs the Operation.</param>
[ProtocolMessage("configuration.apply-raw")]
public sealed record ConfigApplyRaw(
    PzConfigFile File,
    string? BaselineHash,
    string CorrelationId) : AgentCommand;
