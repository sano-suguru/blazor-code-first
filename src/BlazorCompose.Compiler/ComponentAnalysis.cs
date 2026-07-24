using BlazorCompose.Compiler.Diagnostics;

namespace BlazorCompose.Compiler;

/// <summary>
/// The symbol-free, value-equal result of analyzing one candidate component's <c>Body</c> in the
/// syntax-provider transform, before call-site expansion.
/// </summary>
/// <remarks>
/// This is the boundary that keeps <see cref="Microsoft.CodeAnalysis.ISymbol"/>,
/// <see cref="Microsoft.CodeAnalysis.SemanticModel"/>, and
/// <see cref="Microsoft.CodeAnalysis.Compilation"/> out of the cached incremental pipeline: all
/// semantic work (resolving <c>BlazorCompose.Html</c> symbols and classifying the body) happens in the
/// transform that produces this value, and only value data flows onward to be combined with the
/// composable registry. <see cref="Template"/> is <see langword="null"/> when the body is not a
/// recognized statically-sequenceable construct. <see cref="BodyDiagnostics"/> carries every diagnostic
/// normalization recorded — both errors that reject the body and warnings (for example BC3002) that do
/// not — so downstream emission gates on <see cref="DiagnosticInfo.IsError"/> severity rather than on
/// this collection being non-empty.
/// </remarks>
internal sealed record ComponentAnalysis(
    string HintName,
    string ClassName,
    string? Namespace,
    EquatableArray<string> InheritanceKeys,
    RenderTemplateNode? Template,
    EquatableArray<DiagnosticInfo> BodyDiagnostics);
