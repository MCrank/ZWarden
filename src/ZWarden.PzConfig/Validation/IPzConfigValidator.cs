namespace ZWarden.PzConfig.Validation;

/// <summary>
/// Validates a parsed document against ZWarden's own schema (ADR 0010), non-destructively. It never
/// mutates the model and never fails the read — it returns findings: <see cref="PzDiagnosticSeverity.Error"/>
/// for a known key with a wrong-typed or out-of-range value or a missing required key,
/// <see cref="PzDiagnosticSeverity.Info"/> for a key with no schema entry (preserved, not dropped).
/// Validation is separate from parsing so a file can parse cleanly yet still be reported as invalid,
/// and so the schema can grow without touching the readers.
/// </summary>
public interface IPzConfigValidator
{
    /// <summary>Returns the validation findings for a document, most-structural first; empty if clean.</summary>
    IReadOnlyList<PzConfigDiagnostic> Validate(IPzConfigDocument document);
}
