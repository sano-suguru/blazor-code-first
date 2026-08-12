namespace BlazorCodeFirst.CompilerServices;

/// <summary>
/// The one member generated code calls that is not part of the design-time surface. Generated
/// <c>RenderView</c> bodies live in the consumer's assembly, so they cannot read <see cref="View"/>'s
/// internal fragment field directly.
/// </summary>
/// <remarks>
/// Named and placed after <c>Microsoft.AspNetCore.Components.CompilerServices.RuntimeHelpers</c>, which
/// Razor's generated code calls for the same reason. Not part of the authoring surface: BCF3029 does not
/// see it, because <c>KnownSymbols.IsDesignTimeApiMember</c> answers for <c>Html</c>, <c>Decorations</c>
/// and the inert types' own members, and this is none of them.
/// </remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class ViewRuntime
{
    /// <summary>
    /// The fragment <paramref name="view"/> renders, or <see langword="null"/> when it carries none.
    /// <c>RenderTreeBuilder.AddContent(int, RenderFragment?)</c> emits nothing for null, so the Opaque
    /// emission needs no null test of its own.
    /// </summary>
    public static Microsoft.AspNetCore.Components.RenderFragment? FragmentOf(View view) => view.Fragment;
}
