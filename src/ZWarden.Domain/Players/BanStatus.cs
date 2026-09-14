namespace ZWarden.Domain.Players;

/// <summary>The state of a <see cref="BanRecord"/> in ZWarden's advisory ban registry (F19, ADR 0027).</summary>
public enum BanStatus
{
    /// <summary>The ban is in force as far as ZWarden knows — it was issued through ZWarden and not since lifted
    /// here. Not a guarantee of PZ's authoritative state (a ban issued in-game or from the console never appears,
    /// and a ban ZWarden issued may not have taken effect — ADR 0027).</summary>
    Active,

    /// <summary>The ban was lifted through ZWarden (an unban was issued for this user on this Server).</summary>
    Lifted,
}
