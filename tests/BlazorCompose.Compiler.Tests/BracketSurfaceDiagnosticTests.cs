using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCompose.Compiler.Tests;

/// <summary>
/// Diagnostics on the bracket surface, verified on healthy <em>and</em> broken bodies.
/// </summary>
/// <remarks>
/// Reproducing the baselines only proves the paths that succeed.  A rewrite that silently stopped reporting
/// a diagnostic would leave every baseline green, so each diagnostic whose reporting site moves — or whose
/// channel order reverses — is asserted here directly.
/// </remarks>
public sealed class BracketSurfaceDiagnosticTests
{
    private const string CardSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class Card : ComponentBase
        {
            [Parameter] public string Title { get; set; } = "";
            [Parameter] public RenderFragment? ChildContent { get; set; }
            [Parameter] public RenderFragment? Footer { get; set; }
        }
        """;

    /// <summary>A component with no <c>ChildContent</c> at all, so children cannot be bound to it.</summary>
    private const string PlainSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class Plain : ComponentBase
        {
            [Parameter] public string Title { get; set; } = "";
        }
        """;

    private static ImmutableArray<Diagnostic> Run(string body, string members = "") =>
        BracketSurfaceShim.RunGenerator(HostFiles(body, members)).Diagnostics;

    /// <summary>As <see cref="Run"/>, for a body that is deliberately not valid C#.</summary>
    private static ImmutableArray<Diagnostic> RunWithExpectedErrors(string body, string members = "") =>
        BracketSurfaceShim.RunGeneratorWithExpectedErrors(HostFiles(body, members)).Diagnostics;

    private static (string Path, string Source)[] HostFiles(string body, string members) =>
        [
            ("Host.cs", $$"""
                using System.Collections.Generic;
                using BlazorCompose;
                using static BlazorCompose.Html;

                namespace T;

                public partial class Host : ComposeComponentBase
                {
                    {{members}}
                    protected override View Body => {{body}};
                }
                """),
            ("Card.cs", CardSource),
            ("Plain.cs", PlainSource),
        ];

    [Fact]
    public void ComponentIndexer_TargetWithoutChildContent_ReportsBC3013()
    {
        var diagnostics = Run("""Component<Plain>()["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3013" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ComponentIndexer_TargetWithChildContent_IsAccepted()
    {
        var diagnostics = Run("""Component<Card>()["x"]""");

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void FragmentParamThenChildren_SameSlot_ReportsBC3007()
    {
        // The indexer returns View, so .Param cannot follow the brackets and children come last. That
        // reverses which ChildContent channel is filled first: on the method surface children were always
        // syntactically first and the duplicate was caught by the .Param arm's HasBinding check, so the
        // indexer arm has to perform its own or BC3007 goes silent.
        var diagnostics = Run("""Component<Card>().Param(c => c.ChildContent, Div["y"])["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3007" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ScalarNullParamThenChildren_SameSlot_ReportsBC3007()
    {
        // `null` binds to the scalar overload (View is a struct), so this is the cross-channel case.
        var diagnostics = Run("""Component<Card>().Param(c => c.ChildContent, null)["x"]""");

        Assert.Contains(diagnostics, static d => d.Id == "BC3007");
    }

    [Fact]
    public void OtherFragmentParamThenChildren_IsAccepted()
    {
        // A different slot is not a duplicate: the indexer arm must append to the existing slots, not
        // reject them or replace them.
        var diagnostics = Run("""Component<Card>().Param(c => c.Footer, Div["f"])["x"]""");

        Assert.DoesNotContain(diagnostics, static d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ComponentIndexer_UnresolvedTypeArgument_ReportsBC3012Once()
    {
        var diagnostics = RunWithExpectedErrors("""Component<Missing>()["x"]""");

        Assert.Equal(1, diagnostics.Count(static d => d.Id == "BC3012"));
        Assert.DoesNotContain(diagnostics, static d => d.Id == "BC1003");
    }
}
