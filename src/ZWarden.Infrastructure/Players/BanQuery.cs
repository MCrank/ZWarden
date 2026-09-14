using ZWarden.Application.Players;
using ZWarden.Domain.Ids;
using ZWarden.Domain.Players;

namespace ZWarden.Infrastructure.Players;

/// <summary>
/// The default <see cref="IBanQuery"/> (F19): maps the tenant-scoped <see cref="BanRecordRepository"/> reads into
/// display views. Tenant scoping is by construction (the repository never leaves the filtered query root).
/// </summary>
public sealed class BanQuery : IBanQuery
{
    private readonly BanRecordRepository _bans;

    public BanQuery(BanRecordRepository bans)
    {
        ArgumentNullException.ThrowIfNull(bans);
        _bans = bans;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<BanView>> ListForServerAsync(ServerId server, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<BanRecord> records = await _bans.ListForServerAsync(server, cancellationToken).ConfigureAwait(false);
        return records
            .Select(b => new BanView(b.Username, b.Reason, b.Status == BanStatus.Active, b.IssuedAt))
            .ToList();
    }
}
