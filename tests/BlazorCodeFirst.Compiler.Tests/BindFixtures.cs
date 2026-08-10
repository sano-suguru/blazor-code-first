namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// The generated spellings a binding produces, and the template that produces them, for every test that
/// asserts on one.
/// </summary>
/// <remarks>
/// <para>
/// One copy per spelling, for the reason <see cref="SequenceArguments"/> gives about sequence regexes: a
/// second private copy in another file is a hand-maintained link between two literals, and the file whose
/// output is never compiled is the one that drifts. Two layers now assert on these — the emitter tests,
/// which inspect text, and <c>HtmlBindGeneratorTests</c>, which compiles it — so the compiled assertion is
/// what keeps the spelling honest, and it can only do that while both read the same constant.
/// </para>
/// <para>
/// Deliberately not read from <c>RenderViewEmitter</c>'s own constants. An assertion built out of the code
/// under test passes whatever that code emits, including a spelling the C# compiler would reject in the
/// generated file, which is the one failure these strings exist to catch.
/// </para>
/// </remarks>
internal static class BindFixtures
{
    /// <summary>
    /// <c>CreateBinder</c> up to its setter argument, as the emitter writes it: the static call, because
    /// it is an extension method on <c>EventCallbackFactory</c> and the generated file carries no
    /// <c>using</c> directives, so Razor's instance spelling would be CS1061 there.
    /// </summary>
    internal const string CreateBinder =
        "global::Microsoft.AspNetCore.Components.EventCallbackFactoryBinderExtensions.CreateBinder("
        + "global::Microsoft.AspNetCore.Components.EventCallback.Factory, this, ";

    /// <summary>
    /// <c>CreateInferredBindSetter</c> up to its first argument, which wraps an asynchronous setter.
    /// </summary>
    internal const string CreateInferredBindSetter =
        "global::Microsoft.AspNetCore.Components.CompilerServices.RuntimeHelpers"
        + ".CreateInferredBindSetter(";

    /// <summary>
    /// A <see cref="BindTemplate"/> in the getter-only form, whose setter the binder derives by inverting
    /// <paramref name="value"/>. The bound type is <see langword="string"/>, which no assertion on this
    /// form reads: only the synchronous shape's cast names it.
    /// </summary>
    internal static BindTemplate Inverted(string attributeName, string eventName, string value) =>
        new(
            attributeName,
            eventName,
            ExpressionTemplate.Literal(value),
            "global::System.String",
            Setter: null,
            SetterIsAsynchronous: false);
}
