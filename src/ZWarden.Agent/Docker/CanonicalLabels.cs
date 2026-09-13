namespace ZWarden.Agent.Docker;

/// <summary>
/// The canonical <c>io.zwarden.*</c> container label contract (PRD 25), the currency by which the Agent
/// recognises a ZWarden.PZServer container and refuses everything else. The three <c>*Value</c> constants are
/// baked into the image at build time (F12); <see cref="ServerId"/> and <see cref="AgentId"/> are stamped by
/// the Agent at <c>create</c> time (F13). A container is ZWarden's only when the whole baked set is present
/// and correct — a section-level or name-based guess is never enough (trust-boundaries.md §4).
/// </summary>
public static class CanonicalLabels
{
    /// <summary>Marks a container as ZWarden-managed. Baked at build; value is <see cref="ManagedValue"/>.</summary>
    public const string Managed = "io.zwarden.managed";

    /// <summary>The managed runtime kind. Baked at build; value is <see cref="RuntimeValue"/>.</summary>
    public const string Runtime = "io.zwarden.runtime";

    /// <summary>The label-contract schema version. Baked at build; value is <see cref="SchemaVersionValue"/>.</summary>
    public const string SchemaVersion = "io.zwarden.schema-version";

    /// <summary>The <c>srv-</c> id of the Server this container hosts. Stamped by the Agent at create.</summary>
    public const string ServerId = "io.zwarden.server-id";

    /// <summary>The <c>agt-</c> id of the Agent that owns this container. Stamped by the Agent at create.</summary>
    public const string AgentId = "io.zwarden.agent-id";

    /// <summary>The required value of <see cref="Managed"/>.</summary>
    public const string ManagedValue = "true";

    /// <summary>The required value of <see cref="Runtime"/>.</summary>
    public const string RuntimeValue = "project-zomboid";

    /// <summary>The required value of <see cref="SchemaVersion"/> for this build.</summary>
    public const string SchemaVersionValue = "1";
}
