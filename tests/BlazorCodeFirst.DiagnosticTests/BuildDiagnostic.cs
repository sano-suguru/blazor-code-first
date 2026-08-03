namespace BlazorCodeFirst.DiagnosticTests;

/// <summary>
/// One diagnostic exactly as the compiler reported it in a real build, read from the SARIF log csc
/// writes for <c>ErrorLog</c>. <see cref="FilePath"/> is empty and the positions are zero for a
/// diagnostic reported with no location.
/// </summary>
public sealed record BuildDiagnostic(
    string Id,
    string Severity,
    string FilePath,
    int Line,
    int Column,
    int EndLine,
    int EndColumn,
    string Message)
{
    /// <summary>The file name alone, which is how the tests identify a fixture source file.</summary>
    public string FileName => FilePath.Length == 0 ? string.Empty : Path.GetFileName(FilePath);

    public bool HasLocation => FilePath.Length > 0 && Line > 0 && Column > 0;

    public override string ToString() =>
        HasLocation
            ? $"{FileName}({Line},{Column}): {Severity} {Id}: {Message}"
            : $"(no location): {Severity} {Id}: {Message}";
}
