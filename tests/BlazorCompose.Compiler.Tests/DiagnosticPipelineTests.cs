using System.Linq;
using Microsoft.CodeAnalysis;

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
}
