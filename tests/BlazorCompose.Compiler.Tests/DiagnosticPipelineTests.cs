using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace BlazorCompose.Compiler.Tests;

public sealed class DiagnosticPipelineTests
{
    [Fact]
    public void ComponentBody_WithBadKeyAndUnkeyableContent_ReportsBothBc3002AndBc3003()
    {
        // A constant-key ForEach (BC3002 warning) beside a region-rooted-content ForEach (BC3003 error)
        // in the same Body. The warning must not be dropped by the co-located error.
        const string source = """
            using System.Collections.Generic;
            using static BlazorCompose.Html;
            public partial class P : BlazorCompose.ComposeComponentBase
            {
                private readonly List<int> _xs = new();
                private readonly List<Group> _groups = new();
                protected override BlazorCompose.View Body =>
                    Div(
                        ForEach(_xs, key: _ => 0, content: x => Span(x.ToString())),
                        ForEach(_groups, key: g => g.Id, content: g =>
                            ForEach(g.Items, key: i => i.Id, content: i => Span(i.Name))));
                private sealed record Item(int Id, string Name);
                private sealed record Group(int Id, List<Item> Items);
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3002" && d.Severity == DiagnosticSeverity.Warning);
        Assert.Contains(result.Diagnostics, d => d.Id == "BC3003" && d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ComponentBody_UnrecognizedConstruct_ReportsBc1003AndNoSource()
    {
        // A non-factory, non-composable call returning View reaches the model stage with a null template.
        const string source = """
            using static BlazorCompose.Html;
            public partial class P : BlazorCompose.ComposeComponentBase
            {
                protected override BlazorCompose.View Body => Opaque();
                private static BlazorCompose.View Opaque() => default;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "BC1003" && d.Severity == DiagnosticSeverity.Error);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void ComponentBody_WithBc3004_DoesNotAlsoReportBc1003()
    {
        const string source = """
            using System.Collections.Generic;
            using static BlazorCompose.Html;
            public partial class P : BlazorCompose.ComposeComponentBase
            {
                private readonly List<int> _xs = new();
                protected override BlazorCompose.View Body => ForEach(_xs, key: x => x, content: Render);
                private static BlazorCompose.View Render(int x) => Span(x.ToString());
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);

        Assert.Contains(result.Diagnostics, d => d.Id == "BC3004");
        Assert.DoesNotContain(result.Diagnostics, d => d.Id == "BC1003");
    }

    [Fact]
    public void Bc1003_Location_CoversTheUntranslatableExpression()
    {
        // BC1003 was reported with Location.None, so a real build printed it as `CSC(1,1)`: the one
        // diagnostic that explains an unanalyzable body could not be navigated to (#77).
        const string source = """
            using static BlazorCompose.Html;
            public partial class P : BlazorCompose.ComposeComponentBase
            {
                protected override BlazorCompose.View Body => Opaque();
                private static BlazorCompose.View Opaque() => default;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BC1003");

        Assert.Equal("Test.cs", diagnostic.Location.GetLineSpan().Path);
        Assert.Equal("Opaque()", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Bc1003_Location_PointsAtTheInnermostUntranslatableExpression()
    {
        // The whole Body is untranslatable because one leaf is. Blaming the outermost factory would
        // squiggle the entire expression and leave the author to find the leaf; the deepest expression
        // that failed to translate is the one to click.
        const string source = """
            using static BlazorCompose.Html;
            public partial class P : BlazorCompose.ComposeComponentBase
            {
                protected override BlazorCompose.View Body =>
                    Div(
                        Span("fine"),
                        Section(Opaque()));
                private static BlazorCompose.View Opaque() => default;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BC1003");

        Assert.Equal("Opaque()", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Bc1003_Location_ReachesInsideAForEachContentLambda()
    {
        // Control-flow factories analyze their content through the same recursive entry point, so the
        // innermost-wins rule has to survive a lambda body rather than stopping at the ForEach call.
        const string source = """
            using System.Collections.Generic;
            using static BlazorCompose.Html;
            public partial class P : BlazorCompose.ComposeComponentBase
            {
                private readonly List<int> _xs = new();
                protected override BlazorCompose.View Body =>
                    ForEach(_xs, key: x => x, content: x => Div(Opaque()));
                private static BlazorCompose.View Opaque() => default;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BC1003");

        Assert.Equal("Opaque()", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Bc1003_Location_DistinguishesTwoComponentsInOneFile()
    {
        // The shape the issue describes: several components in one file. A location that does not
        // separate them is no better than none.
        const string source = """
            using static BlazorCompose.Html;
            public partial class First : BlazorCompose.ComposeComponentBase
            {
                protected override BlazorCompose.View Body => Div("fine");
            }
            public partial class Second : BlazorCompose.ComposeComponentBase
            {
                protected override BlazorCompose.View Body => Opaque();
                private static BlazorCompose.View Opaque() => default;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BC1003");

        var line = diagnostic.Location.GetLineSpan().StartLinePosition.Line;
        Assert.Equal(7, line);
        Assert.Equal("Opaque()", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public void Bc1003_Location_CoversAnUntranslatableChrome()
    {
        // A layout reports BC1003 from the same place; Chrome must be located like Body.
        const string source = """
            using static BlazorCompose.Html;
            public partial class Shell : BlazorCompose.ComposeLayoutBase
            {
                protected override BlazorCompose.View Chrome => Main(Opaque());
                private static BlazorCompose.View Opaque() => default;
            }
            """;

        var result = CompilationTestHost.RunGenerator(source);
        var diagnostic = Assert.Single(result.Diagnostics, d => d.Id == "BC1003");

        Assert.Equal("Opaque()", SourceText.From(source).ToString(diagnostic.Location.SourceSpan));
    }
}
