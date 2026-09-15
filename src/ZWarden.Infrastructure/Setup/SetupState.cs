using Microsoft.EntityFrameworkCore;
using ZWarden.Application.Setup;
using ZWarden.Domain.Setup;
using ZWarden.Infrastructure.Persistence;

namespace ZWarden.Infrastructure.Setup;

/// <summary>
/// The <see cref="ISetupState"/> implementation over the <see cref="InstallState"/> singleton (F33). Reads
/// and writes the one well-known row (<see cref="InstallState.DefaultId"/>) directly — a sanctioned unscoped
/// read, since the record is not tenant-owned (ADR 0016). Completion is memoized in
/// <see cref="SetupCompletionSignal"/> so the first-run gate's hot path costs nothing once setup is done.
/// </summary>
public sealed class SetupState : ISetupState
{
    private readonly ZWardenDbContext _context;
    private readonly SetupCompletionSignal _signal;
    private readonly TimeProvider _clock;

    public SetupState(ZWardenDbContext context, SetupCompletionSignal signal, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(clock);
        _context = context;
        _signal = signal;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<bool> IsSetupCompleteAsync(CancellationToken cancellationToken = default)
    {
        if (_signal.IsComplete)
        {
            return true;
        }

        InstallState? state = await LoadAsync(cancellationToken).ConfigureAwait(false);
        bool complete = state?.IsSetupComplete ?? false;
        if (complete)
        {
            _signal.MarkComplete();
        }

        return complete;
    }

    /// <inheritdoc />
    public async Task<TlsMode?> GetTlsModeAsync(CancellationToken cancellationToken = default)
        => (await LoadAsync(cancellationToken).ConfigureAwait(false))?.TlsMode;

    /// <inheritdoc />
    public async Task RecordTlsModeAsync(TlsMode mode, CancellationToken cancellationToken = default)
    {
        InstallState state = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
        state.RecordTlsMode(mode);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task MarkSetupCompleteAsync(CancellationToken cancellationToken = default)
    {
        InstallState state = await LoadOrCreateAsync(cancellationToken).ConfigureAwait(false);
        state.MarkSetupComplete(_clock.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _signal.MarkComplete();
    }

    private Task<InstallState?> LoadAsync(CancellationToken cancellationToken)
        => _context.Set<InstallState>()
            .FirstOrDefaultAsync(s => s.Id == InstallState.DefaultId, cancellationToken);

    private async Task<InstallState> LoadOrCreateAsync(CancellationToken cancellationToken)
    {
        InstallState? state = await LoadAsync(cancellationToken).ConfigureAwait(false);
        if (state is null)
        {
            state = InstallState.CreateDefault();
            _context.Add(state);
        }

        return state;
    }
}
