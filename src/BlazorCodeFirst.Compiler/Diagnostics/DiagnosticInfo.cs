using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCodeFirst.Compiler.Diagnostics;

/// <summary>
/// Value-equal, symbol-free representation of a diagnostic captured while source syntax was
/// available. Incremental generator values must never carry <see cref="Diagnostic"/>,
/// <see cref="Location"/>, <see cref="SyntaxTree"/>, <see cref="ISymbol"/>, or
/// <see cref="SemanticModel"/>; this record captures only the primitive coordinates required to
/// reconstruct a location in <c>RegisterSourceOutput</c>.
/// </summary>
internal sealed record DiagnosticInfo(
    string Id,
    string FilePath,
    TextSpan Span,
    LinePositionSpan LineSpan,
    EquatableArray<string> MessageArguments)
{
    /// <summary>Captures a <see cref="DiagnosticInfo"/> from a live <see cref="Location"/> and a descriptor.</summary>
    public static DiagnosticInfo Create(
        DiagnosticDescriptor descriptor,
        Location location,
        ImmutableArray<string> messageArguments)
    {
        var lineSpan = location.GetLineSpan();
        return new DiagnosticInfo(
            descriptor.Id,
            lineSpan.Path ?? string.Empty,
            location.SourceSpan,
            lineSpan.Span,
            messageArguments);
    }

    /// <summary>True when this diagnostic's descriptor has <see cref="DiagnosticSeverity.Error"/> default severity.</summary>
    public bool IsError => DiagnosticDescriptors.ById(Id).DefaultSeverity == DiagnosticSeverity.Error;

    /// <summary>Reconstructs a <see cref="Diagnostic"/>, resolving the descriptor from <see cref="Id"/>.</summary>
    public Diagnostic ToDiagnostic()
    {
        var location = Location.Create(FilePath, Span, LineSpan);
        return Diagnostic.Create(DiagnosticDescriptors.ById(Id), location, MessageArguments.ToArray<object?>());
    }
}
