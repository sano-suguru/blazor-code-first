using BlazorCompose.Compiler.Diagnostics;

namespace BlazorCompose.Compiler;

/// <summary>
/// The symbol-free, value-equal result of analyzing one candidate component's design-time expression
/// (<c>Body</c> or <c>Chrome</c>) in the syntax-provider transform, before call-site expansion.
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
/// <param name="DesignTimeExpressionName">
/// The name of the design-time expression this component declares — <c>Body</c> on a component,
/// <c>Chrome</c> on a layout. Carried as a value so diagnostics raised after the semantic stage can
/// name the real expression instead of hardcoding one of them.
/// </param>
/// <param name="TypeParameters">
/// The component's own type-parameter names in declaration order, empty for a non-generic component.
/// Names are taken verbatim from the symbol because every partial declaration of a generic type must use
/// the same type-parameter names in the same order (CS0264), so the generated part cannot rename them.
/// </param>
internal sealed record ComponentAnalysis(
    string HintName,
    string ClassName,
    EquatableArray<string> TypeParameters,
    string? Namespace,
    string DesignTimeExpressionName,
    EquatableArray<string> InheritanceKeys,
    RenderTemplateNode? Template,
    EquatableArray<DiagnosticInfo> BodyDiagnostics);
