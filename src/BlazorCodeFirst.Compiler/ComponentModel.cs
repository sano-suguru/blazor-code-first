using System.Collections.Immutable;
using BlazorCodeFirst.Compiler.Diagnostics;

namespace BlazorCodeFirst.Compiler;

/// <summary>
/// Represents a discovered Compose component together with the statically analyzed
/// render-node tree extracted from its design-time expression (<c>Body</c> or <c>Chrome</c>).
/// </summary>
/// <remarks>
/// All fields contain only strings, primitive values, <see cref="EquatableArray{T}"/> wrappers,
/// and nested <see cref="RenderNode"/> instances so that the record can participate in Roslyn's
/// incremental generator equality checks without holding references to syntax nodes, symbols,
/// semantic models, or compilations.
/// <c>ComponentModel</c> instances are only created when the design-time expression is fully
/// SSC-analyzable; a null <c>RootNode</c> is therefore not representable.
/// </remarks>
/// <param name="TypeParameters">
/// The component's own type-parameter names in declaration order, empty for a non-generic component.
/// Names are taken verbatim from the symbol because every partial declaration of a generic type must use
/// the same type-parameter names in the same order (CS0264), so the generated part cannot rename them.
/// </param>
internal sealed record ComponentModel(
    string HintName,
    string ClassName,
    EquatableArray<string> TypeParameters,
    string? Namespace,
    RenderNode RootNode);

/// <summary>
/// The value-equal outcome of modeling a candidate component: either a final <see cref="Model"/>
/// (with no diagnostics) or a set of call-site <see cref="Diagnostics"/> (with a null model).
/// </summary>
/// <remarks>
/// Diagnostics are captured as symbol-free <see cref="DiagnosticInfo"/> so the result stays value-equal
/// across incremental runs; they are deliberately excluded from <see cref="ComponentModel"/> equality and
/// reconstructed into Roslyn diagnostics only inside the source-output callback.  <see cref="Diagnostics"/>
/// is an <see cref="EquatableArray{T}"/>, which treats a <c>default</c> array as equal to an empty one, so
/// equality never depends on whether the no-diagnostic case is stored as <c>default</c> or empty.
/// </remarks>
internal sealed record ComponentModelResult
{
    public ComponentModelResult(ComponentModel? model, ImmutableArray<DiagnosticInfo> diagnostics)
    {
        Model = model;
        Diagnostics = diagnostics;
    }

    public ComponentModel? Model { get; }

    public EquatableArray<DiagnosticInfo> Diagnostics { get; }
}
