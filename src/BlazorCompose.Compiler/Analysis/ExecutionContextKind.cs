namespace BlazorCompose.Compiler.Analysis;

/// <summary>
/// Classifies a <c>Body</c> sub-expression into the three compiler pipeline paths and
/// the execution contexts recognized for diagnostic purposes.
/// </summary>
internal enum ExecutionContextKind
{
    /// <summary>
    /// Statically sequenceable construct: a direct factory call, decorator, <c>If</c>, keyed <c>ForEach</c>,
    /// nested SSC expression, or statically expanded <c>[Composable]</c> call.
    /// The generator emits <c>RenderTreeBuilder</c> calls with compile-time sequence constants.
    /// </summary>
    Ssc,

    /// <summary>
    /// Transplantable construct: native control flow (<c>if</c>, <c>foreach</c>, <c>switch</c>).
    /// The generator transplants the syntax into the generated method and isolates it with a
    /// statically numbered Blazor region.
    /// </summary>
    Transplantable,

    /// <summary>
    /// Opaque call: a non-<c>[Composable]</c> method returning <c>View</c> or any shape that cannot
    /// be statically analyzed.  The generator evaluates it at runtime as a <c>RenderFragment</c>-backed
    /// <c>View</c>, isolates it in a region, and reports diagnostic BC2001.
    /// </summary>
    Opaque,

    /// <summary>
    /// Deferred event handler: the recognized (sole, reduced) argument of a Html-mirror
    /// <c>View.OnClick(...)</c> call. Code in this context executes after rendering (in response to a
    /// DOM event), so state mutations are expected and must not be reported as BC3001.
    /// </summary>
    DeferredEventHandler,
}
