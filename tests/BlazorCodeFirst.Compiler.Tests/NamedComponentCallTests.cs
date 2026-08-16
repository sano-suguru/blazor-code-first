using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace BlazorCodeFirst.Compiler.Tests;

/// <summary>
/// Holds the two claims §Naming a component call in <c>site/content/components-and-reuse.md</c> makes
/// about this compiler (#169): its figure renders what writing the component call out twice renders,
/// and the component a part names may come from another assembly even though the part may not.
/// </summary>
/// <remarks>
/// <para>
/// The figure is read from the published document rather than restated here, on the same grounds as
/// <see cref="LandingPageFigureTests"/> and <see cref="PackageReadmeTests"/>: a figure that claims
/// what the compiler emits is only checked if the file a reader reads is the file that is compiled.
/// <c>site/</c> is outside <c>BlazorCodeFirst.slnx</c> and cannot reference the compiler, so the
/// assertion lives on this side of that boundary. The section's other figures make no such claim and
/// are read the way every other example on the page is — by a person.
/// </para>
/// <para>
/// The passage says two further things that are general compiler behaviour rather than claims about
/// its own figure, and both are held where that behaviour lives:
/// <c>ComponentInteropTests.Component_ParamInsideAViewPartCalledTwice_ReportsOnceAtThePart</c> and
/// <c>ViewPartContentGeneratorTests.ContentTakingViewPart_WhoseSlotSitsInAComponentsBrackets_FillsThatComponentsChildContent</c>.
/// A later rewrite of the prose must not be able to take either with it.
/// </para>
/// </remarks>
public sealed partial class NamedComponentCallTests
{
    private static readonly string DocumentPath = Path.Combine(
        GeneratedSourceSnapshot.FindRepositoryRoot(), "site", "content", "components-and-reuse.md");

    /// <summary>The section whose figure this holds to the compiler.</summary>
    private const string Heading = "### Naming a component call";

    /// <summary>
    /// What the figure leaves out: the usings every example on the page shares, and the component it
    /// names. <c>StatusBadge</c> carries exactly the two parameters the figure binds, so a figure that
    /// grew a third would fail here rather than be compiled against a component nobody wrote.
    /// </summary>
    private const string Surroundings = """
        using BlazorCodeFirst;
        using Microsoft.AspNetCore.Components;
        using static BlazorCodeFirst.Html;

        public partial class StatusBadge : BodyComponentBase
        {
            [Parameter] public string Label { get; set; } = "";
            [Parameter] public bool Compact { get; set; }

            protected override View Body => Span[Label];
        }

        """;

    /// <summary>
    /// The spelling the passage says the figure is equivalent to: the same component opened twice by
    /// hand, with the arguments the two calls pass written into the bindings.
    /// </summary>
    private const string WrittenOutTwice = """
        public partial class Dashboard : BodyComponentBase
        {
            protected override View Body =>
                Div[
                    Component<StatusBadge>()
                        .Param(b => b.Label, "hello")
                        .Param(b => b.Compact, false),
                    Component<StatusBadge>()
                        .Param(b => b.Label, "x")
                        .Param(b => b.Compact, true)];
        }
        """;

    [Fact]
    public void TheFigure_RendersWhatWritingTheComponentCallTwiceRenders()
    {
        var figure = ReadFigure();

        var named = CompilationTestHost.RunGenerator(Surroundings + figure);
        CompilationTestHost.AssertNoDiagnostics(named);
        CompilationTestHost.AssertOutputCompiles(named);
        CompilationTestHost.AssertGeneratedOutputHasNoWarnings(named);

        var byHand = CompilationTestHost.RunGenerator(Surroundings + WrittenOutTwice);
        CompilationTestHost.AssertNoDiagnostics(byHand);

        var expected = Frames(CallerSource(byHand));
        var actual = Frames(CallerSource(named));

        // Equal empty lists would report two components that render nothing as agreement.
        Assert.True(
            expected.Length > 0,
            "The written-out spelling emits no frames, so there is nothing to hold the figure to.");

        Assert.True(
            expected.SequenceEqual(actual),
            "The figure does not render what writing the component call twice renders.\n\n" +
            $"by hand:\n{string.Join("\n", expected)}\n\nfigure:\n{string.Join("\n", actual)}");
    }

    /// <summary>
    /// The half of the assembly rule the passage states as an opportunity: the component being named
    /// is an ordinary <c>OpenComponent&lt;T&gt;</c> type argument, so it resolves from metadata like
    /// any other, and a package's component takes a name without the package knowing.
    /// </summary>
    /// <remarks>
    /// The other half — that the part itself cannot cross the boundary — is BCF1002's own subject and
    /// is held by <see cref="ViewPartDefinitionTests"/>.
    /// </remarks>
    [Fact]
    public void APartNamingAComponentFromAnotherAssembly_ExpandsIntoTheCaller()
    {
        var packaged = CompilationTestHost.CompileToMetadataReference("""
            using System.Collections.Generic;
            using Microsoft.AspNetCore.Components;

            namespace Vendor;

            public sealed class VendorGrid<T> : ComponentBase
            {
                [Parameter] public IEnumerable<T>? Items { get; set; }
                [Parameter] public bool Dense { get; set; }
            }
            """, "Vendor");

        const string source = """
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using Vendor;
            using static BlazorCodeFirst.Html;

            public static class Widgets
            {
                [ViewPart]
                public static View OrderGrid(IEnumerable<string> orders, bool dense = true) =>
                    Component<VendorGrid<string>>()
                        .Param(g => g.Items, orders)
                        .Param(g => g.Dense, dense);
            }

            public partial class Dashboard : BodyComponentBase
            {
                private readonly List<string> _orders = [];

                protected override View Body => Div[Widgets.OrderGrid(_orders)];
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            CompilationTestHost.CreateCompilation([("Test.cs", source)], packaged));

        CompilationTestHost.AssertNoDiagnostics(result);
        CompilationTestHost.AssertOutputCompiles(result);

        var generated = CallerSource(result);
        Assert.Contains("__builder.OpenComponent<global::Vendor.VendorGrid<string>>(1);", generated);
        Assert.Contains("__builder.AddComponentParameter(3, \"Dense\", ", generated);
    }

    /// <summary>
    /// The figure the passage is written around: the first <c>csharp</c> fence under
    /// <see cref="Heading"/>. A heading that no longer exists, or a section that no longer opens with
    /// a figure, fails here rather than silently compiling some other page's code.
    /// </summary>
    private static string ReadFigure()
    {
        var document = File.ReadAllText(DocumentPath).Replace("\r\n", "\n", StringComparison.Ordinal);

        var headingIndex = document.IndexOf("\n" + Heading + "\n", StringComparison.Ordinal);
        Assert.True(
            headingIndex >= 0,
            $"site/content/components-and-reuse.md has no '{Heading}' heading. The passage this test " +
            "holds to the compiler moved; point it at wherever it lives now.");

        const string Fence = "\n```csharp\n";
        var open = document.IndexOf(Fence, headingIndex, StringComparison.Ordinal);
        var next = document.IndexOf("\n#", headingIndex + Heading.Length, StringComparison.Ordinal);

        Assert.True(
            open >= 0 && (next < 0 || open < next),
            $"'{Heading}' no longer opens with a C# figure, so there is nothing to compile.");

        var content = open + Fence.Length;
        var close = document.IndexOf("\n```", content, StringComparison.Ordinal);
        Assert.True(close >= 0, $"The figure under '{Heading}' has an unterminated fence.");

        return document[content..close];
    }

    /// <summary>
    /// The <c>RenderView</c> of the one component the figure declares, told apart from
    /// <c>StatusBadge</c>'s by name so the figure may call its own component whatever it likes.
    /// </summary>
    private static string CallerSource(GeneratorRunResult result) =>
        Assert.Single(result.GeneratedSources
            .Where(static source => source.HintName != "StatusBadge.g.cs"))
            .SourceText.ToString();

    /// <summary>
    /// The <c>__builder</c> calls in <paramref name="generated"/>, in order, with every argument local
    /// the expansion introduced read back as the value assigned to it. That substitution is the whole
    /// of what the two spellings are allowed to differ by: an expansion binds each argument to a local
    /// first, and the passage's claim is about the frames, not about the statements between them.
    /// </summary>
    private static ImmutableArray<string> Frames(string generated)
    {
        var values = ArgumentLocal().Matches(generated)
            .ToDictionary(
                static local => local.Groups["name"].Value,
                static local => local.Groups["value"].Value,
                StringComparer.Ordinal);

        return
        [
            .. BuilderCalls.InTextOrder(generated)
                .Select(line => ArgumentName().Replace(
                    line,
                    match => values.TryGetValue(match.Value, out var value) ? value : match.Value)),
        ];
    }

    /// <summary>The local an expansion binds one argument to, declared ahead of the frames it feeds.</summary>
    [GeneratedRegex(
        @"^[^\S\n]*[\w\.<>\[\]?:]+ (?<name>__bcf_arg_\d+_\d+) = (?<value>[^\r\n]+);[^\S\n]*$",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture)]
    private static partial Regex ArgumentLocal();

    [GeneratedRegex(@"__bcf_arg_\d+_\d+")]
    private static partial Regex ArgumentName();
}
