using System.Linq;
using Microsoft.CodeAnalysis;

namespace BlazorCodeFirst.Compiler.Tests;

public sealed class ComponentTemplateGeneratorTests
{
    private const string TemplateTargetSource = """
        using Microsoft.AspNetCore.Components;
        namespace T;
        public class TemplateTarget : ComponentBase
        {
            [Parameter] public RenderFragment<int>? RowTemplate { get; set; }
        }
        """;

    [Fact]
    public void Template_OverloadsCompileWithoutCSharpErrors()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public sealed class Host
            {
                public View Build() => Fragment(
                    Component<TemplateTarget>().Template(c => c.RowTemplate, Div["x"]),
                    Component<TemplateTarget>().Template(
                        c => c.RowTemplate,
                        context => Span[context.ToString()]));
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        Assert.DoesNotContain(
            result.OutputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error),
            d => d.Id.StartsWith("CS", System.StringComparison.Ordinal));
    }

    [Fact]
    public void RenderFragmentOfTScalarParams_CompileAndAreEmittedVerbatim()
    {
        const string host = """
            using BlazorCodeFirst;
            using Microsoft.AspNetCore.Components;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private RenderFragment<int>? _fragment;
                protected override View Body => Fragment(
                    Component<TemplateTarget>().Param(c => c.RowTemplate, null),
                    Component<TemplateTarget>().Param(c => c.RowTemplate, default),
                    Component<TemplateTarget>().Param(c => c.RowTemplate, default(RenderFragment<int>)),
                    Component<TemplateTarget>().Param(c => c.RowTemplate, (RenderFragment<int>?)null),
                    Component<TemplateTarget>().Param(c => c.RowTemplate, _fragment));
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        CompilationTestHost.AssertOutputCompiles(result);
        var generated = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("__builder.AddComponentParameter(1, \"RowTemplate\", null);", generated);
        Assert.Contains("__builder.AddComponentParameter(3, \"RowTemplate\", default);", generated);
        Assert.Contains(
            "__builder.AddComponentParameter(5, \"RowTemplate\", "
                + "default(global::Microsoft.AspNetCore.Components.RenderFragment<int>));",
            generated);
        Assert.Contains(
            "__builder.AddComponentParameter(7, \"RowTemplate\", "
                + "(global::Microsoft.AspNetCore.Components.RenderFragment<int>?)null);",
            generated);
        Assert.Contains("__builder.AddComponentParameter(9, \"RowTemplate\", _fragment);", generated);
    }
}
