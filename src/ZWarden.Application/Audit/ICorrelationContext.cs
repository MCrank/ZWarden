namespace ZWarden.Application.Audit;

/// <summary>
/// The ambient correlation id tying related audit events together — the events of one request or operation
/// share it, so an operator can reconstruct a sequence from the viewer without threading an id through call
/// sites (F6; ADR 0019). It is optional: <c>null</c> when no correlation is in scope.
/// </summary>
public interface ICorrelationContext
{
    /// <summary>The current correlation id, or <c>null</c> when none is in scope.</summary>
    string? CurrentCorrelationId { get; }
}
