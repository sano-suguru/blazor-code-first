using BlazorCodeFirst.Compiler.Diagnostics;

namespace BlazorCodeFirst.Compiler;

/// <summary>
/// The symbol-free, value-equal result of analyzing one candidate component's design-time expression
/// (<c>Body</c> or <c>Chrome</c>) in the syntax-provider transform, before call-site expansion.
/// </summary>
/// <remarks>
/// This is the boundary that keeps <see cref="Microsoft.CodeAnalysis.ISymbol"/>,
/// <see cref="Microsoft.CodeAnalysis.SemanticModel"/>, and
/// <see cref="Microsoft.CodeAnalysis.Compilation"/> out of the cached incremental pipeline: all
/// semantic work (resolving <c>BlazorCodeFirst.Html</c> symbols and classifying the body) happens in the
/// transform that produces this value, and only value data flows onward to be combined with the
/// view part registry. <see cref="Template"/> is <see langword="null"/> when the body is not a
/// recognized statically-sequenceable construct. <see cref="BodyDiagnostics"/> carries every diagnostic
/// normalization recorded, both errors that reject the body and warnings (for example BCF3002) that do
/// not, so downstream emission gates on <see cref="DiagnosticInfo.IsError"/> severity rather than on
/// this collection being non-empty.
/// </remarks>
/// <param name="HintName">The generated file's hint name, passed straight to <c>AddSource</c>.</param>
/// <param name="ClassName">The component class's own name, without namespace or type parameters.</param>
/// <param name="TypeParameters">
/// The component's own type-parameter names in declaration order, empty for a non-generic component.
/// Names are taken verbatim from the symbol because every partial declaration of a generic type must use
/// the same type-parameter names in the same order (CS0264), so the generated part cannot rename them.
/// </param>
/// <param name="Namespace">The component's namespace, or <see langword="null"/> for one declared in the global namespace.</param>
/// <param name="FilePath">
/// The declaring syntax tree's file path (<c>classDeclaration.SyntaxTree.FilePath</c>), symbol-free.
/// Scope is a file-unit concept (ARCHITECTURE.md §2.7(F)): this is what
/// <see cref="ComponentModelFactory.Expand"/> looks up in the <c>CssScopeRegistry</c> to find this
/// component's own <c>.cs.css</c> scope, and what a later BCF3041 orphan check compares against.
/// </param>
/// <param name="DesignTimeExpressionName">
/// The name of the design-time expression this component declares, <c>Body</c> on a component,
/// <c>Chrome</c> on a layout. Carried as a value so diagnostics raised after the semantic stage can
/// name the real expression instead of hardcoding one of them.
/// </param>
/// <param name="InheritanceKeys">
/// Method keys of every <c>ViewPart</c>-attributed method reachable through the component's base classes,
/// used to resolve an unqualified view-part call against inherited candidates.
/// </param>
/// <param name="Template">The classified render template, or <see langword="null"/> when the body is not a recognized statically-sequenceable construct.</param>
/// <param name="BodyDiagnostics">Every diagnostic normalization recorded while classifying the body, both errors and non-rejecting warnings.</param>
/// <param name="FailureLocation">
/// Where to blame when <paramref name="Template"/> is <see langword="null"/> because classification
/// failed: the innermost expression that could not be translated. Carried as symbol-free coordinates for
/// the same reason <see cref="DiagnosticInfo"/> is, BCF1003 is decided after the semantic stage, where no
/// syntax survives, and without this it had no location at all (#77). <see langword="null"/> whenever a
/// template was produced, so a healthy component contributes nothing new to the incremental cache key,
/// and also on the shapes rejected before classification (non-partial, nested, untranslatable getter),
/// which carry their own located error instead. Deliberately not defaulted: every construction site has
/// to state which of those it is, so a fifth null-template shape cannot silently reintroduce the
/// location-less report by forgetting the argument.
/// </param>
internal sealed record ComponentAnalysis(
    string HintName,
    string ClassName,
    EquatableArray<string> TypeParameters,
    string? Namespace,
    string FilePath,
    string DesignTimeExpressionName,
    EquatableArray<string> InheritanceKeys,
    RenderTemplateNode? Template,
    EquatableArray<DiagnosticInfo> BodyDiagnostics,
    TemplateLocation? FailureLocation);
