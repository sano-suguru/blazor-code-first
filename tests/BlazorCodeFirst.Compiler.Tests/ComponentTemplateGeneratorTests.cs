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
            [Parameter] public RenderFragment<string>? StringTemplate { get; set; }
            public int Id { get; set; }
        }
        """;

    [Fact]
    public void ContextIgnoredTemplate_IsLoweredThroughTypedComponentSlot()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, Div["x"]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("__builder.OpenComponent<global::T.TemplateTarget>(0);", code);
        Assert.Contains(
            "__builder.AddComponentParameter(1, \"RowTemplate\", " +
                "(global::Microsoft.AspNetCore.Components.RenderFragment<global::System.Int32>)((_) => (__builder) =>",
            code);
        Assert.Contains("__builder.AddMarkupContent(2, \"<div>x</div>\");", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ContextualTemplate_SubstitutesTheLambdaParameterAndEmitsTypedNestedLambdas()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Span[$"value={context}"]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains(
            "(global::Microsoft.AspNetCore.Components.RenderFragment<global::System.Int32>)" +
                "((__bcf_context_1) => (__builder) =>",
            code);
        Assert.Contains("__builder.AddContent(3, $\"value={__bcf_context_1}\");", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ContextualTemplate_NestedTemplate_UsesDistinctScopedContextVariables()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Component<TemplateTarget>().Template(c => c.RowTemplate, inner =>
                            Span[$"{context}:{inner}"]));
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("((__bcf_context_1) => (__builder) =>", code);
        Assert.Contains("((__bcf_context_2) => (__builder) =>", code);
        Assert.Contains("$\"{__bcf_context_1}:{__bcf_context_2}\"", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ContextualTemplate_InsideForEach_KeepsItemAndContextScopesDistinct()
    {
        const string host = """
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private readonly List<TemplateTarget> _targets = [];

                protected override View Body =>
                    ForEach(_targets, t => t.Id, t =>
                        Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                            Span[$"{t.Id}:{context}"]));
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("foreach (var __bcf_item_0 in _targets)", code);
        Assert.Contains("((__bcf_context_2) => (__builder) =>", code);
        Assert.Contains("$\"{__bcf_item_0.Id}:{__bcf_context_2}\"", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ContextualTemplate_ContainingForEach_KeepsContextAndItemScopesDistinct()
    {
        const string host = """
            using System.Collections.Generic;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private readonly List<TemplateTarget> _targets = [];

                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Div[ForEach(_targets, t => t.Id, t =>
                            Span[$"{context}:{t.Id}"])]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("((__bcf_context_1) => (__builder) =>", code);
        Assert.Contains("foreach (var __bcf_item_2 in _targets)", code);
        Assert.Contains("$\"{__bcf_context_1}:{__bcf_item_2.Id}\"", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ContextualTemplate_ComposableCall_SubstitutesContextThroughTheComposableArgument()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Format(context));

                [Composable]
                private static View Format(int value) => Span[$"formatted={value}"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("int __bcf_arg_1_0 = __bcf_context_1;", code);
        Assert.Contains("$\"formatted={__bcf_arg_1_0}\"", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ContextualTemplate_InsideComposableWithValueParameter_OwnsTheNextHoleOrdinal()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private readonly string _prefix = "row";

                protected override View Body => BuildTemplate(_prefix);

                [Composable]
                private static View BuildTemplate(string prefix) =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Span[$"{prefix}:{context}"]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("string __bcf_arg_0_0 = _prefix;", code);
        Assert.Contains("((__bcf_context_2) => (__builder) =>", code);
        Assert.Contains("$\"{__bcf_arg_0_0}:{__bcf_context_2}\"", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ContextualTemplate_ExpressionForms_SubstituteOnlyTheBoundParameterSymbol()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;

            public static class ContextExtensions
            {
                public static string Describe(this string? value) => value ?? "null";
            }

            public partial class Host : BodyComponentBase
            {
                private readonly string __bcf_context_1 = "member";

                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.StringTemplate, context =>
                        Div[
                            Span[nameof(context)],
                            Span[context?.ToString()],
                            Span[$"context={context}"],
                            Span[context.Describe()],
                            Span[$"member={__bcf_context_1}; context={context}"]]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("((__bcf_context_1) => (__builder) =>", code);
        Assert.Contains("__bcf_context_1?.ToString()", code);
        Assert.Contains("$\"context={__bcf_context_1}\"", code);
        Assert.Contains("global::T.ContextExtensions.Describe(__bcf_context_1)", code);
        Assert.Contains("this.__bcf_context_1", code);
        Assert.DoesNotContain("nameof(context)", code);
        Assert.Contains("<span>context</span>", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

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
