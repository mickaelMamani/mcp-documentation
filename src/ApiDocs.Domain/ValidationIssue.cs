namespace ApiDocs.Domain;

internal enum ValidationSeverity
{
    /// <summary>The file is still indexed; the report records the problem.</summary>
    Warning,

    /// <summary>The file is rejected (DOC-FORMAT §7); the rest of the index is still built.</summary>
    Error,
}

/// <summary>One violation of a DOC-FORMAT §7 rule, reported per file and per rule.</summary>
internal sealed record ValidationIssue(
    string FilePath,
    string Rule,
    string Message,
    ValidationSeverity Severity = ValidationSeverity.Error,
    int? Line = null)
{
    public override string ToString() =>
        Line is { } line
            ? $"{Severity.ToString().ToUpperInvariant()} {FilePath}:{line} [{Rule}] {Message}"
            : $"{Severity.ToString().ToUpperInvariant()} {FilePath} [{Rule}] {Message}";
}
