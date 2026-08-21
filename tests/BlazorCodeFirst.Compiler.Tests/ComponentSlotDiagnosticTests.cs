using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

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
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body => {{body}};
            }
            """;

        return CompilationTestHost.RunGenerator(("Targets.cs", TargetsSource), ("Host.cs", host));
    }

    [Fact]
    public void ComponentWithChildren_TypeHasNoChildContent_ReportsBCF3013()
    {
        var result = Run("Component<NoChildContent>()[Div[\"x\"]]");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3013" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedSources, s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void ComponentWithChildren_ChildContentIsGenericFragment_IsAccepted()
    {
        // The brackets supply the outer lambda that discards the context, which is the emission
        // .Template's context-ignoring overload already writes (#322).
        var result = Run("Component<TypedChildContent>()[Div[\"x\"]]");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3013");
        Assert.Contains(result.GeneratedSources, s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void ComponentWithChildren_ChildContentLacksParameterAttribute_ReportsBCF3013()
    {
        var result = Run("Component<UnmarkedChildContent>()[Div[\"x\"]]");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3013");
    }

    [Fact]
    public void ComponentWithNoChildren_TypeHasNoChildContent_IsAccepted()
    {
        // BCF3013 is about supplying children, not about the type's shape.
        var result = Run("Component<NoChildContent>().Param(c => c.Footer, Div[\"f\"])");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3013");
        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ScalarParam_ViewValue_ReportsBCF3014()
    {
        // Binds through the generic Param and would render silently wrong: the inert default(View) is
        // assigned to an object parameter with NO runtime exception at all.
        var result = Run("Component<Card>().Param(c => c.Payload, Div[\"x\"])");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3014" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.GeneratedSources, s => s.HintName.Contains("Host"));
    }

    [Fact]
    public void ScalarParam_ViewValue_WithUnresolvedTypeArgument_ReportsBCF3014AndNotBCF3015()
    {
        var result = Run("Component<Card>().Param(c => c.Payload, Div[typeof(Unresolved).Name])");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3014");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3015");
    }

    [Fact]
    public void ScalarParam_ComponentViewValue_ReportsBCF3014()
    {
        var result = Run("Component<Card>().Param(c => c.Payload, Component<Card>())");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3014");
    }

    [Fact]
    public void ScalarParam_ExplicitViewTypeArgument_ReportsBCF3014()
    {
        var result = Run("Component<Card>().Param<View>(c => c.ChildContent, Div[\"x\"])");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3014");
    }

    [Fact]
    public void ScalarParam_OrdinaryValue_DoesNotReportBCF3014()
    {
        var result = Run("Component<Card>().Param(c => c.Title, \"t\")");

        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3014");
    }

    /// <summary>
    /// The receiver-recursion guard at the top of <c>ClassifyComponentParameter</c>: a chained
    /// <c>.Param</c> whose receiver is itself an already-rejected <c>.Param</c> call (here, a duplicate
    /// <c>Title</c> binding, BCF3007) never resolves to a <see cref="ComponentNode"/>, so this
    /// call's own value never reaches generated code either. That value carries an unresolved reference,
    /// which the failure scanner must not go on to report — the same contract every other rejection in
    /// this file keeps.
    /// </summary>
    [Fact]
    public void ChainedParam_WithRejectedReceiver_AndUnresolvedTypeArgument_ReportsBCF3007AndNotBCF3015()
    {
        var result = Run(
            "Component<Card>().Param(c => c.Title, \"a\").Param(c => c.Title, \"b\")"
            + ".Param(c => c.Payload, Div[typeof(Unresolved).Name])");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3007");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3015");
    }

    /// <summary>
    /// An explicit empty collection literal reaches <c>TryAppendChildContentSlot</c> with a real
    /// zero-length <c>children</c>, unlike bare empty brackets, which fail earlier for an unrelated
    /// reason. Zero children is a no-op, not a failure: nothing was written to require a
    /// <c>ChildContent</c> parameter or duplicate an existing one (#487).
    /// </summary>
    [Fact]
    public void ExplicitEmptyChildrenLiteral_OnAComponentWithNoChildContent_IsAccepted()
    {
        var result = Run("Component<NoChildContent>()[[]]");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ChildrenAndFragmentParam_SameSlot_ReportsBCF3007()
    {
        var result = Run("Component<Card>().Param(c => c.ChildContent, Div[\"y\"])[Div[\"x\"]]");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3007" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ChildrenAndNullScalarParam_SameSlot_ReportsBCF3007()
    {
        // `null` binds to the SCALAR overload (View is a struct), so this is the cross-channel case.
        var result = Run("Component<Card>().Param(c => c.ChildContent, null)[Div[\"x\"]]");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3007");
    }

    [Fact]
    public void ChildrenAndTemplate_SameSlot_ReportsBCF3007()
    {
        // Brackets reach a generic ChildContent, so this is the same mistake as children plus a fragment
        // .Param: two channels write one parameter and Blazor applies the last. The indexer arm's own
        // duplicate check is what reports it, and it runs before the slot is built, so the narrowed
        // BCF3013 never sees this call (#322).
        var result = Run(
            "Component<TypedChildContent>().Template(c => c.ChildContent, Div[\"y\"])[Div[\"x\"]]");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3007" && d.Severity == DiagnosticSeverity.Error);
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BCF3013");
    }

    [Fact]
    public void TwoFragmentParams_SameSlot_ReportsBCF3007()
    {
        var result = Run(
            "Component<Card>().Param(c => c.ChildContent, Div[\"x\"]).Param(c => c.ChildContent, Div[\"y\"])");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3007");
    }

    [Fact]
    public void SlotContainingRegionRootedContent_IsAccepted()
    {
        // A slot is a fragment, so region-rooted content is fine, no key is ever applied to it.
        var result = Run("Component<Card>()[If(true, () => Div[\"x\"])]");

        Assert.DoesNotContain(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ForEachInsideSlot_WithRegionRootedContent_ReportsBCF3003()
    {
        // The ForEach nested inside the slot still needs a keyable content root; without the slot walk
        // this degrades to a diagnostic-free BCF1003.
        var result = Run(
            "Component<Card>()[ForEach(new[] { 1, 2 }, i => i, i => If(true, () => Div[\"x\"]))]");

        Assert.Contains(result.Diagnostics, d => d.Id == "BCF3003");
    }
}
