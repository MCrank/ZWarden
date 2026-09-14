namespace ZWarden.PzConfig;

/// <summary>Why a <see cref="IPzConfigDocument.TrySetValue"/> did or did not apply.</summary>
public enum PzEditStatus
{
    /// <summary>The value was replaced.</summary>
    Ok,

    /// <summary>No settable value exists at the given path.</summary>
    PathNotFound,

    /// <summary>The path resolves to a table; the surgical writer replaces scalar values, not tables.</summary>
    PathIsTable,

    /// <summary>The document was not opened from bytes (it carries no parse backing) and cannot be edited.</summary>
    NotEditable,
}

/// <summary>
/// The first-class outcome of a surgical value edit (F20b, ADR 0010). A refused edit is a value a
/// caller inspects — path absent, path is a table, document not editable — never an exception, in the
/// same spirit as F20a's "the file did not parse" result.
/// </summary>
public sealed record PzConfigEditResult
{
    private PzConfigEditResult(PzEditStatus status, string? message)
    {
        Status = status;
        Message = message;
    }

    /// <summary>Why the edit did or did not apply.</summary>
    public PzEditStatus Status { get; }

    /// <summary>A human-readable reason, or <see langword="null"/> on success.</summary>
    public string? Message { get; }

    /// <summary><see langword="true"/> when the value was replaced.</summary>
    public bool Ok => Status == PzEditStatus.Ok;

    /// <summary>The value was replaced.</summary>
    public static PzConfigEditResult Success { get; } = new(PzEditStatus.Ok, null);

    /// <summary>The document carries no parse backing and cannot be edited.</summary>
    public static PzConfigEditResult NotEditable { get; } =
        new(PzEditStatus.NotEditable, "This document was not opened from bytes and cannot be edited.");

    /// <summary>No settable value exists at <paramref name="path"/>.</summary>
    public static PzConfigEditResult PathNotFound(string path) =>
        new(PzEditStatus.PathNotFound, $"No configuration value at path '{path}'.");

    /// <summary>The value at <paramref name="path"/> is a table, which the surgical writer does not replace.</summary>
    public static PzConfigEditResult PathIsTable(string path) =>
        new(PzEditStatus.PathIsTable, $"The path '{path}' is a table, not a settable value.");
}
