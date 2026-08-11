using BlazorCodeFirst;

namespace BlazorCodeFirst.Runtime.Tests;

/// <summary>
/// The inertness contract for <see cref="ElementView"/>, matching what <c>ViewTests</c> and
/// <c>ViewConversionTests</c> pin for <see cref="View"/>: the members exist so the generator can read
/// them, and at runtime they do nothing and yield the default value.
/// </summary>
public sealed class ElementViewTests
{
    [Fact]
    public void Indexer_IsInert_ReturnsDefaultView()
    {
        ElementView builder = default;

        Assert.Equal(default, builder["a", "b"]);
    }

    [Fact]
    public void Indexer_WithASingleChild_IsInert()
    {
        ElementView builder = default;
        View child = default;

        Assert.Equal(default, builder[child]);
    }

    [Fact]
    public void ImplicitConversionToView_IsInert()
    {
        View view = default(ElementView);

        Assert.Equal(default, view);
    }

    [Fact]
    public void ComponentViewIndexer_IsInert_ReturnsDefaultView()
    {
        ComponentView<ProbeComponent> component = default;

        Assert.Equal(default, component["child"]);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1812:Avoid uninstantiated internal classes",
        Justification = "This probes the ComponentView<TComponent> indexer, which is inert design-time " +
            "syntax: TComponent is only ever a generic type argument here, never constructed, so the " +
            "analyzer's 'apparently never instantiated' finding is expected rather than dead code.")]
    private sealed class ProbeComponent : Microsoft.AspNetCore.Components.ComponentBase;
}
