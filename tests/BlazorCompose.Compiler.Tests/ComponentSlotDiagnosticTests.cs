using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

public sealed class ComponentSlotDiagnosticTests
{
    private const string TargetsSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class Card : ComponentBase
        {
            [Parameter] public string Title { get; set; } = "";
            [Parameter] public RenderFragment? ChildContent { get; set; }
            [Parameter] public object? Payload { get; set; }
        }
        public class NoChildContent : ComponentBase
        {
            [Parameter] public RenderFragment? Footer { get; set; }
        }
        public class TypedChildContent : ComponentBase
        {
            [Parameter] public RenderFragment<int>? ChildContent { get; set; }
        }
        public class UnmarkedChildContent : ComponentBase
        {
            public RenderFragment? ChildContent { get; set; }
        }
        """;

    private static GeneratorRunResult Run(string body)
    {
        string host = $$"""
            using BlazorCompose;
            using Microsoft.AspNetCore.Components;
            using static BlazorCompose.Html;
            namespace T;
            public partial class Host : ComposeComponentBase
            {
                protected override View Body => {{body}};
            }
            """;

        return CompilationTestHost.RunGenerator(("Targets.cs", TargetsSource), ("Host.cs", host));
    }

    [Fact]
    public void ComponentWithChildren_TypeHasNoChildContent_ReportsBC3013()
    {
        var result = Run("Component<NoChildContent>(Div(\"x\"))");

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3013" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedSources, s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void ComponentWithChildren_ChildContentIsGenericFragment_ReportsBC3013()
    {
        // A non-generic RenderFragment cannot bind to RenderFragment<T>: without this the author gets
        // "Unable to cast RenderFragment to RenderFragment`1[System.Int32]" at runtime.
        var result = Run("Component<TypedChildContent>(Div(\"x\"))");

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3013");
    }

    [Fact]
    public void ComponentWithChildren_ChildContentLacksParameterAttribute_ReportsBC3013()
    {
        var result = Run("Component<UnmarkedChildContent>(Div(\"x\"))");

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3013");
    }

    [Fact]
    public void ComponentWithNoChildren_TypeHasNoChildContent_IsAccepted()
    {
        // BC3013 is about supplying children, not about the type's shape.
        var result = Run("Component<NoChildContent>().Param(c => c.Footer, Div(\"f\"))");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BC3013");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ScalarParam_ViewValue_ReportsBC3014()
    {
        // Binds through the generic Param and would render silently wrong: the inert default(View) is
        // assigned to an object parameter with NO runtime exception at all.
        var result = Run("Component<Card>().Param(c => c.Payload, Div(\"x\"))");

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3014" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedSources, s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void ScalarParam_ComponentViewValue_ReportsBC3014()
    {
        var result = Run("Component<Card>().Param(c => c.Payload, Component<Card>())");

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3014");
    }

    [Fact]
    public void ScalarParam_ExplicitViewTypeArgument_ReportsBC3014()
    {
        var result = Run("Component<Card>().Param<View>(c => c.ChildContent, Div(\"x\"))");

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3014");
    }

    [Fact]
    public void ScalarParam_OrdinaryValue_DoesNotReportBC3014()
    {
        var result = Run("Component<Card>().Param(c => c.Title, \"t\")");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BC3014");
    }

    [Fact]
    public void ChildrenAndFragmentParam_SameSlot_ReportsBC3007()
    {
        var result = Run("Component<Card>(Div(\"x\")).Param(c => c.ChildContent, Div(\"y\"))");

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3007" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ChildrenAndNullScalarParam_SameSlot_ReportsBC3007()
    {
        // `null` binds to the SCALAR overload (View is a struct), so this is the cross-channel case.
        var result = Run("Component<Card>(Div(\"x\")).Param(c => c.ChildContent, null)");

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3007");
    }

    [Fact]
    public void TwoFragmentParams_SameSlot_ReportsBC3007()
    {
        var result = Run(
            "Component<Card>().Param(c => c.ChildContent, Div(\"x\")).Param(c => c.ChildContent, Div(\"y\"))");

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3007");
    }

    [Fact]
    public void SlotContainingRegionRootedContent_IsAccepted()
    {
        // A slot is a fragment, so region-rooted content is fine — no key is ever applied to it.
        var result = Run("Component<Card>(If(true, () => Div(\"x\")))");

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ForEachInsideSlot_WithRegionRootedContent_ReportsBC3003()
    {
        // The ForEach nested inside the slot still needs a keyable content root; without the slot walk
        // this degrades to a diagnostic-free BC1003.
        var result = Run(
            "Component<Card>(ForEach(new[] { 1, 2 }, i => i, i => If(true, () => Div(\"x\"))))");

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3003");
    }
}
