namespace ZWarden.PzConfig.Revisions;

/// <summary>Why a target value could not be restored by a surgical (values-only) edit.</summary>
public enum PzRestoreObstacleKind
{
    /// <summary>The target has a scalar at this path that the current file lacks; the writer does not add keys.</summary>
    AddNotSupported,

    /// <summary>The current file has a scalar at this path that the target lacks; the writer does not remove keys.</summary>
    RemoveNotSupported,
}

/// <summary>A structural difference a values-only restore cannot apply, and where it is.</summary>
/// <param name="Path">The dotted path of the difference.</param>
/// <param name="Kind">Whether restoring would require adding or removing a key.</param>
public sealed record PzRestoreObstacle(string Path, PzRestoreObstacleKind Kind);

/// <summary>
/// The result of planning a revision restore (F20b, ADR 0011): the surgical value edits that make the
/// current file match the target snapshot, plus the structural differences that a values-only writer
/// cannot apply (adding or removing a key — PZ owns the key set). The plan is non-destructive; a caller
/// applies <see cref="Edits"/> through <see cref="IPzConfigDocument.TrySetValue"/>.
/// </summary>
/// <param name="Edits">The value edits to apply, sorted by path.</param>
/// <param name="Obstacles">Structural differences the writer cannot apply, sorted by path.</param>
public sealed record PzRestorePlan(IReadOnlyList<PzScalarEntry> Edits, IReadOnlyList<PzRestoreObstacle> Obstacles)
{
    /// <summary><see langword="true"/> when the file's values already match the target with no obstacles.</summary>
    public bool IsClean => Edits.Count == 0 && Obstacles.Count == 0;
}
