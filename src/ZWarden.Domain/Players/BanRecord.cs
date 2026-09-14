using ZWarden.Domain.Ids;
using ZWarden.Domain.Tenancy;

namespace ZWarden.Domain.Players;

/// <summary>
/// A record of a player ban ZWarden issued (<c>ban-</c>, F19) — the advisory ban registry (ADR 0027). PZ ships
/// no "list bans" RCON command, so without this record the operator has no ban list to view or unban from. It
/// therefore records <b>the bans ZWarden issued through the control plane</b> — the acting user, the account
/// username, an optional reason, and whether it has since been lifted here — <b>not</b> a mirror of PZ's
/// authoritative user store: a ban issued in-game or from the console never appears, and a ban ZWarden issued
/// may not have taken effect (the Operation and audit trail are authoritative for whether the RCON command
/// succeeded). It is <see cref="ITenantOwned"/> (stamped and filtered by the ambient tenant, ADR 0016) and is
/// scoped to one Server. The username is operator input, validated before it is recorded; never a secret.
/// </summary>
public sealed class BanRecord : ITenantOwned
{
    /// <summary>The greatest length stored for the recorded username / reason (operator input, bounded).</summary>
    public const int MaxUsernameLength = 64;

    /// <summary>The greatest length stored for an optional reason.</summary>
    public const int MaxReasonLength = 200;

    /// <summary>EF / factory use.</summary>
    public BanRecord()
    {
    }

    /// <summary>The ban record identifier (<c>ban-&lt;uuid&gt;</c>).</summary>
    public BanRecordId Id { get; init; } = BanRecordId.New();

    /// <inheritdoc />
    public TenantId TenantId { get; init; }

    /// <summary>The Server the ban applies to.</summary>
    public ServerId ServerId { get; init; }

    /// <summary>The banned account username (operator input, validated before recording; never a secret).</summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>The optional operator-facing reason recorded with the ban.</summary>
    public string? Reason { get; init; }

    /// <summary>The user who issued the ban through ZWarden.</summary>
    public UserId IssuedByUserId { get; init; }

    /// <summary>When the ban was issued through ZWarden (UTC).</summary>
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>Whether the ban is <see cref="BanStatus.Active"/> or has been <see cref="BanStatus.Lifted"/>
    /// through ZWarden.</summary>
    public BanStatus Status { get; private set; } = BanStatus.Active;

    /// <summary>The user who lifted the ban through ZWarden, or <c>null</c> while <see cref="BanStatus.Active"/>.</summary>
    public UserId? LiftedByUserId { get; private set; }

    /// <summary>When the ban was lifted through ZWarden (UTC), or <c>null</c> while <see cref="BanStatus.Active"/>.</summary>
    public DateTimeOffset? LiftedAt { get; private set; }

    /// <summary>Creates an <see cref="BanStatus.Active"/> ban record. The <see cref="TenantId"/> is left unset so
    /// the ownership interceptor stamps the ambient tenant on insert (ADR 0016).</summary>
    public static BanRecord Issue(
        ServerId serverId, string username, string? reason, UserId issuedBy, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return new BanRecord
        {
            Id = BanRecordId.New(),
            ServerId = serverId,
            Username = username,
            Reason = reason,
            IssuedByUserId = issuedBy,
            IssuedAt = now,
            Status = BanStatus.Active,
        };
    }

    /// <summary>Marks an <see cref="BanStatus.Active"/> ban <see cref="BanStatus.Lifted"/>. Idempotent — lifting
    /// an already-lifted record is a no-op, so a repeated unban never errors.</summary>
    public void Lift(UserId liftedBy, DateTimeOffset now)
    {
        if (Status == BanStatus.Lifted)
        {
            return;
        }

        Status = BanStatus.Lifted;
        LiftedByUserId = liftedBy;
        LiftedAt = now;
    }
}
