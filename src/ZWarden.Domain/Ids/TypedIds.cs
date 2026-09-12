namespace ZWarden.Domain.Ids;

// The canonical typed identifiers (PRD 7 + the ten- tenant id from PRD 7A) - 19 in all.
// Each follows the ADR 0014 recipe: a readonly record struct over ITypedId<TSelf>, a unique
// lowercase prefix, and thin delegations to TypedId. The PrefixRegistryTests reflection test
// enforces PRD 7's rules (unique, lowercase-ASCII, never reused) and this exact set.
//
// Adding an entity's id: copy one struct, rename it, give it a new prefix, update the registry
// test's expected set. Nothing else is required.

/// <summary>Identifies a Tenant (PRD 7A). Canonical form <c>ten-&lt;uuid&gt;</c>.</summary>
public readonly record struct TenantId(Guid Value) : ITypedId<TenantId>
{
    /// <inheritdoc />
    public static string Prefix => "ten-";
    /// <inheritdoc />
    public static TenantId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static TenantId New() => TypedId.New<TenantId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static TenantId Parse(string s) => TypedId.Parse<TenantId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out TenantId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<TenantId>(Value);
}

/// <summary>Identifies a User. Canonical form <c>usr-&lt;uuid&gt;</c>.</summary>
public readonly record struct UserId(Guid Value) : ITypedId<UserId>
{
    /// <inheritdoc />
    public static string Prefix => "usr-";
    /// <inheritdoc />
    public static UserId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static UserId New() => TypedId.New<UserId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static UserId Parse(string s) => TypedId.Parse<UserId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out UserId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<UserId>(Value);
}

/// <summary>Identifies a Role. Canonical form <c>rol-&lt;uuid&gt;</c>.</summary>
public readonly record struct RoleId(Guid Value) : ITypedId<RoleId>
{
    /// <inheritdoc />
    public static string Prefix => "rol-";
    /// <inheritdoc />
    public static RoleId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static RoleId New() => TypedId.New<RoleId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static RoleId Parse(string s) => TypedId.Parse<RoleId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out RoleId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<RoleId>(Value);
}

/// <summary>Identifies a ZWarden.Agent. Canonical form <c>agt-&lt;uuid&gt;</c>.</summary>
public readonly record struct AgentId(Guid Value) : ITypedId<AgentId>
{
    /// <inheritdoc />
    public static string Prefix => "agt-";
    /// <inheritdoc />
    public static AgentId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static AgentId New() => TypedId.New<AgentId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static AgentId Parse(string s) => TypedId.Parse<AgentId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out AgentId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<AgentId>(Value);
}

/// <summary>Identifies a Server (a PZ server instance). Canonical form <c>srv-&lt;uuid&gt;</c>.</summary>
public readonly record struct ServerId(Guid Value) : ITypedId<ServerId>
{
    /// <inheritdoc />
    public static string Prefix => "srv-";
    /// <inheritdoc />
    public static ServerId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static ServerId New() => TypedId.New<ServerId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static ServerId Parse(string s) => TypedId.Parse<ServerId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out ServerId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<ServerId>(Value);
}

/// <summary>Identifies an Operation. Canonical form <c>op-&lt;uuid&gt;</c>.</summary>
public readonly record struct OperationId(Guid Value) : ITypedId<OperationId>
{
    /// <inheritdoc />
    public static string Prefix => "op-";
    /// <inheritdoc />
    public static OperationId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static OperationId New() => TypedId.New<OperationId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static OperationId Parse(string s) => TypedId.Parse<OperationId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out OperationId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<OperationId>(Value);
}

/// <summary>Identifies an audit event. Canonical form <c>aud-&lt;uuid&gt;</c>.</summary>
public readonly record struct AuditEventId(Guid Value) : ITypedId<AuditEventId>
{
    /// <inheritdoc />
    public static string Prefix => "aud-";
    /// <inheritdoc />
    public static AuditEventId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static AuditEventId New() => TypedId.New<AuditEventId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static AuditEventId Parse(string s) => TypedId.Parse<AuditEventId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out AuditEventId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<AuditEventId>(Value);
}

/// <summary>Identifies a Backup. Canonical form <c>bkp-&lt;uuid&gt;</c>.</summary>
public readonly record struct BackupId(Guid Value) : ITypedId<BackupId>
{
    /// <inheritdoc />
    public static string Prefix => "bkp-";
    /// <inheritdoc />
    public static BackupId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static BackupId New() => TypedId.New<BackupId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static BackupId Parse(string s) => TypedId.Parse<BackupId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out BackupId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<BackupId>(Value);
}

/// <summary>Identifies a sanitized support package. Canonical form <c>diag-&lt;uuid&gt;</c>.</summary>
public readonly record struct DiagnosticPackageId(Guid Value) : ITypedId<DiagnosticPackageId>
{
    /// <inheritdoc />
    public static string Prefix => "diag-";
    /// <inheritdoc />
    public static DiagnosticPackageId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static DiagnosticPackageId New() => TypedId.New<DiagnosticPackageId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static DiagnosticPackageId Parse(string s) => TypedId.Parse<DiagnosticPackageId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out DiagnosticPackageId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<DiagnosticPackageId>(Value);
}

/// <summary>Identifies an Enrollment. Canonical form <c>enr-&lt;uuid&gt;</c>.</summary>
public readonly record struct EnrollmentId(Guid Value) : ITypedId<EnrollmentId>
{
    /// <inheritdoc />
    public static string Prefix => "enr-";
    /// <inheritdoc />
    public static EnrollmentId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static EnrollmentId New() => TypedId.New<EnrollmentId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static EnrollmentId Parse(string s) => TypedId.Parse<EnrollmentId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out EnrollmentId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<EnrollmentId>(Value);
}

/// <summary>Identifies a configuration revision. Canonical form <c>cfg-&lt;uuid&gt;</c>.</summary>
public readonly record struct ConfigurationRevisionId(Guid Value) : ITypedId<ConfigurationRevisionId>
{
    /// <inheritdoc />
    public static string Prefix => "cfg-";
    /// <inheritdoc />
    public static ConfigurationRevisionId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static ConfigurationRevisionId New() => TypedId.New<ConfigurationRevisionId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static ConfigurationRevisionId Parse(string s) => TypedId.Parse<ConfigurationRevisionId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out ConfigurationRevisionId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<ConfigurationRevisionId>(Value);
}

/// <summary>Identifies a Mod (a PZ Mod id, distinct from a Workshop item). Canonical form <c>mod-&lt;uuid&gt;</c>.</summary>
public readonly record struct ModId(Guid Value) : ITypedId<ModId>
{
    /// <inheritdoc />
    public static string Prefix => "mod-";
    /// <inheritdoc />
    public static ModId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static ModId New() => TypedId.New<ModId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static ModId Parse(string s) => TypedId.Parse<ModId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out ModId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<ModId>(Value);
}

/// <summary>Identifies a Workshop item (distinct from a Mod). Canonical form <c>wsi-&lt;uuid&gt;</c>.</summary>
public readonly record struct WorkshopItemId(Guid Value) : ITypedId<WorkshopItemId>
{
    /// <inheritdoc />
    public static string Prefix => "wsi-";
    /// <inheritdoc />
    public static WorkshopItemId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static WorkshopItemId New() => TypedId.New<WorkshopItemId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static WorkshopItemId Parse(string s) => TypedId.Parse<WorkshopItemId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out WorkshopItemId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<WorkshopItemId>(Value);
}

/// <summary>Identifies a mod profile. Canonical form <c>mdp-&lt;uuid&gt;</c>.</summary>
public readonly record struct ModProfileId(Guid Value) : ITypedId<ModProfileId>
{
    /// <inheritdoc />
    public static string Prefix => "mdp-";
    /// <inheritdoc />
    public static ModProfileId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static ModProfileId New() => TypedId.New<ModProfileId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static ModProfileId Parse(string s) => TypedId.Parse<ModProfileId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out ModProfileId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<ModProfileId>(Value);
}

/// <summary>Identifies a ban record. Canonical form <c>ban-&lt;uuid&gt;</c>.</summary>
public readonly record struct BanRecordId(Guid Value) : ITypedId<BanRecordId>
{
    /// <inheritdoc />
    public static string Prefix => "ban-";
    /// <inheritdoc />
    public static BanRecordId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static BanRecordId New() => TypedId.New<BanRecordId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static BanRecordId Parse(string s) => TypedId.Parse<BanRecordId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out BanRecordId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<BanRecordId>(Value);
}

/// <summary>Identifies a player record. Canonical form <c>ply-&lt;uuid&gt;</c>.</summary>
public readonly record struct PlayerRecordId(Guid Value) : ITypedId<PlayerRecordId>
{
    /// <inheritdoc />
    public static string Prefix => "ply-";
    /// <inheritdoc />
    public static PlayerRecordId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static PlayerRecordId New() => TypedId.New<PlayerRecordId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static PlayerRecordId Parse(string s) => TypedId.Parse<PlayerRecordId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out PlayerRecordId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<PlayerRecordId>(Value);
}

/// <summary>Identifies a permission assignment. Canonical form <c>prm-&lt;uuid&gt;</c>.</summary>
public readonly record struct PermissionAssignmentId(Guid Value) : ITypedId<PermissionAssignmentId>
{
    /// <inheritdoc />
    public static string Prefix => "prm-";
    /// <inheritdoc />
    public static PermissionAssignmentId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static PermissionAssignmentId New() => TypedId.New<PermissionAssignmentId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static PermissionAssignmentId Parse(string s) => TypedId.Parse<PermissionAssignmentId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out PermissionAssignmentId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<PermissionAssignmentId>(Value);
}

/// <summary>Identifies a certificate record. Canonical form <c>crt-&lt;uuid&gt;</c>.</summary>
public readonly record struct CertificateRecordId(Guid Value) : ITypedId<CertificateRecordId>
{
    /// <inheritdoc />
    public static string Prefix => "crt-";
    /// <inheritdoc />
    public static CertificateRecordId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static CertificateRecordId New() => TypedId.New<CertificateRecordId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static CertificateRecordId Parse(string s) => TypedId.Parse<CertificateRecordId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out CertificateRecordId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<CertificateRecordId>(Value);
}

/// <summary>Identifies a notification. Canonical form <c>ntf-&lt;uuid&gt;</c>.</summary>
public readonly record struct NotificationId(Guid Value) : ITypedId<NotificationId>
{
    /// <inheritdoc />
    public static string Prefix => "ntf-";
    /// <inheritdoc />
    public static NotificationId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static NotificationId New() => TypedId.New<NotificationId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static NotificationId Parse(string s) => TypedId.Parse<NotificationId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out NotificationId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<NotificationId>(Value);
}

/// <summary>Identifies a single protocol message on the Web ↔ Agent wire (F7, PRD 18's
/// <c>MessageId</c>). Canonical form <c>msg-&lt;uuid&gt;</c>; doubles as an envelope dedup key.</summary>
public readonly record struct MessageId(Guid Value) : ITypedId<MessageId>
{
    /// <inheritdoc />
    public static string Prefix => "msg-";
    /// <inheritdoc />
    public static MessageId FromGuid(Guid value) => new(value);
    /// <summary>A fresh, non-empty UUIDv7 id.</summary>
    public static MessageId New() => TypedId.New<MessageId>();
    /// <summary>Parses the canonical form; throws on the wrong prefix or a malformed UUID.</summary>
    public static MessageId Parse(string s) => TypedId.Parse<MessageId>(s);
    /// <summary>Non-throwing parse of the canonical form.</summary>
    public static bool TryParse(string? s, out MessageId id) => TypedId.TryParse(s, out id);
    /// <summary>True when this is the default (unset) id.</summary>
    public bool IsEmpty => Value == Guid.Empty;
    /// <inheritdoc />
    public override string ToString() => TypedId.Format<MessageId>(Value);
}
