using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

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
    public void ContextualTemplate_ViewPartCall_SubstitutesContextThroughTheViewPartArgument()
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

                [ViewPart]
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
    public void ContextualTemplate_InsideViewPartWithValueParameter_OwnsTheNextHoleOrdinal()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private readonly string _prefix = "row";

                protected override View Body => BuildTemplate(_prefix);

                [ViewPart]
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

    /// <summary>
    /// The test above covers the collision qualification for a field. The remaining instance-member
    /// kinds the rule accepts — property, method, and event — are covered here. Each declaration is
    /// spelled exactly like the generated contextual parameter, so an unqualified reference in the
    /// generated lambda would bind to that parameter instead of the containing instance's member. The
    /// context is <c>int</c> and every member is reached through a <c>string</c>- or
    /// <c>Action</c>-typed overload, so a missing <c>this.</c> is a compile error rather than a silent
    /// rebinding.
    /// </summary>
    [Theory]
    [InlineData("private string __bcf_context_1 => \"member\";", "__bcf_context_1")]
    [InlineData("private string __bcf_context_1() => \"member\";", "__bcf_context_1()")]
    [InlineData("private event System.Action? __bcf_context_1;", "__bcf_context_1")]
    public void ContextualTemplate_CollidingInstanceMember_IsQualifiedWithThis(
        string declaration,
        string reference)
    {
        var host = $$"""
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;

            public static class Sink
            {
                public static string Read(string value) => value;
                public static string Read(System.Action? handler) => handler is null ? "null" : "handler";
            }

            public partial class Host : BodyComponentBase
            {
                {{declaration}}

                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Span[Sink.Read({{reference}}) + context.ToString()]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("((__bcf_context_1) => (__builder) =>", code);
        Assert.Contains($"this.{reference}", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// A colliding method named as a handler rather than invoked. The rule's invocation fallback does
    /// not apply here — the identifier's parent is an argument, not an invocation — so qualification
    /// rests entirely on <c>GetOperation</c> yielding a member reference for a method group in a
    /// delegate-conversion position. It does, and this pins that it keeps doing so.
    /// </summary>
    [Fact]
    public void ContextualTemplate_CollidingMethodGroupHandler_IsQualifiedWithThis()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private void __bcf_context_1() { }

                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Button.OnClick(__bcf_context_1)[context.ToString()]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("((__bcf_context_1) => (__builder) =>", code);
        Assert.Contains("this.__bcf_context_1", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// A colliding member read through a member access of its own. The shadowing is the same wherever the
    /// name stands, and the receiver is the position it stands in here: the context is <c>int</c>, so an
    /// unqualified receiver binds to the generated parameter and the call on it does not exist (#392).
    /// </summary>
    [Fact]
    public void ContextualTemplate_CollidingMemberAtReceiverPosition_IsQualifiedWithThis()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private readonly string __bcf_context_1 = "member";

                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Span[__bcf_context_1.ToUpperInvariant() + context.ToString()]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("((__bcf_context_1) => (__builder) =>", code);
        Assert.Contains("this.__bcf_context_1.ToUpperInvariant()", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    /// <summary>
    /// The member name on the left of an object- or nested-initializer assignment names a member of the
    /// object being initialized, not of the containing instance, so <c>this.</c> would not compile there
    /// even though the spelling collides. The same spelling as a bare reference in the same expression is
    /// qualified, which is what distinguishes the two positions.
    /// </summary>
    [Fact]
    public void ContextualTemplate_CollidingInitializerMemberName_IsLeftUnqualified()
    {
        const string host = """
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;

            public sealed class Nested
            {
                public string __bcf_context_1 { get; set; } = "";
            }

            public sealed class Bag
            {
                public string __bcf_context_1 { get; set; } = "";
                public Nested Inner { get; } = new();
            }

            public partial class Host : BodyComponentBase
            {
                private readonly string __bcf_context_1 = "member";

                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Span[new Bag { __bcf_context_1 = "x", Inner = { __bcf_context_1 = "y" } }.Inner.__bcf_context_1
                            + __bcf_context_1
                            + context.ToString()]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("((__bcf_context_1) => (__builder) =>", code);
        Assert.Contains("__bcf_context_1 = \"x\"", code);
        Assert.Contains("__bcf_context_1 = \"y\"", code);
        Assert.DoesNotContain("this.__bcf_context_1 =", code);

        // The containing-instance field in the same expression is still qualified, so the assertion
        // above is about the initializer position and not about qualification being off altogether.
        Assert.Contains("this.__bcf_context_1", code);
        CompilationTestHost.AssertOutputCompiles(result);
    }

    [Fact]
    public void ContextualTemplate_NestedLambdaParameterCannotCaptureGeneratedContextName()
    {
        const string host = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Span[$"{((Func<int, int>)(__bcf_context_1 =>
                            context + __bcf_context_1))(0)}"]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        CompilationTestHost.AssertOutputCompiles(result);
        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("((__bcf_context_1) => (__builder) =>", code);
        Assert.Contains("__bcf_authored_context_0 =>", code);
        Assert.Contains("__bcf_context_1 + __bcf_authored_context_0", code);
        Assert.DoesNotContain("__bcf_context_1 =>", code);
        AssertGeneratedContextReferencesBindOuterParameter(result, "__bcf_context_1");
    }

    [Fact]
    public void ContextualTemplate_ViewPartExpansionKeepsNestedLambdaBindingHygienic()
    {
        const string host = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;

            public static class OffsetExtensions
            {
                public static int Twice(this int value) => value * 2;
            }

            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        AddOffset(context));

                [ViewPart]
                private static View AddOffset(int value) =>
                    Span[$"{((Func<int, int>)(__bcf_context_1 =>
                        value + __bcf_context_1.Twice()))(0)}"];
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        CompilationTestHost.AssertOutputCompiles(result);
        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("int __bcf_arg_1_0 = __bcf_context_1;", code);
        Assert.Contains("__bcf_authored_context_0 =>", code);
        Assert.Contains(
            "__bcf_arg_1_0 + global::T.OffsetExtensions.Twice(__bcf_authored_context_0)",
            code);
        Assert.DoesNotContain("__bcf_context_1 =>", code);
        AssertGeneratedContextReferencesBindOuterParameter(result, "__bcf_context_1");
    }

    [Fact]
    public void ContextualTemplate_NestedLocalAndPatternDeclarationsCannotCaptureGeneratedContextName()
    {
        const string host = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Div[
                            Span[$"{((Func<int>)(() =>
                            {
                                var __bcf_context_1 = 1;
                                return context + __bcf_context_1;
                            }))()}"],
                            Span[$"{(0 is int __bcf_context_1
                                ? context + __bcf_context_1
                                : context)}"]]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        CompilationTestHost.AssertOutputCompiles(result);
        var generated = result.GeneratedSources.Single(s => s.HintName.Contains("Host"));
        var root = generated.SyntaxTree.GetRoot();
        var localName = root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Single(variable => variable.Initializer?.Value.ToString() == "1")
            .Identifier.ValueText;
        var patternName = root.DescendantNodes().OfType<SingleVariableDesignationSyntax>()
            .Single()
            .Identifier.ValueText;

        Assert.StartsWith("__bcf_authored_context_", localName, System.StringComparison.Ordinal);
        Assert.StartsWith("__bcf_authored_context_", patternName, System.StringComparison.Ordinal);
        AssertGeneratedContextReferencesBindOuterParameter(result, "__bcf_context_1");
    }

    [Fact]
    public void ContextualTemplate_HygieneRenameAvoidsExistingAuthorIdentifier()
    {
        const string host = """
            using System;
            using BlazorCodeFirst;
            using static BlazorCodeFirst.Html;
            namespace T;
            public partial class Host : BodyComponentBase
            {
                private readonly int __bcf_authored_context_0 = 10;

                protected override View Body =>
                    Component<TemplateTarget>().Template(c => c.RowTemplate, context =>
                        Span[$"{((Func<int, int>)(__bcf_context_1 =>
                            context + __bcf_context_1 + __bcf_authored_context_0))(0)}"]);
            }
            """;

        var result = CompilationTestHost.RunGenerator(
            ("TemplateTarget.cs", TemplateTargetSource), ("Host.cs", host));

        CompilationTestHost.AssertOutputCompiles(result);
        var code = result.GeneratedSources.Single(s => s.HintName.Contains("Host")).SourceText.ToString();
        Assert.Contains("__bcf_authored_context_0_1 =>", code);
        Assert.Contains(
            "__bcf_context_1 + __bcf_authored_context_0_1 + __bcf_authored_context_0",
            code);
        AssertGeneratedContextReferencesBindOuterParameter(result, "__bcf_context_1");
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
        // Every one of the five carries the resolved value type. The first two are the shapes #377 is
        // about: `null` and `default` have no natural type at all, so what they mean in an `object?` slot
        // is decided by the slot rather than by the parameter they were written for.
        const string fragmentOfInt =
            "(global::Microsoft.AspNetCore.Components.RenderFragment<global::System.Int32>?)";
        Assert.Contains($"__builder.AddComponentParameter(1, \"RowTemplate\", {fragmentOfInt}(null));", generated);
        Assert.Contains($"__builder.AddComponentParameter(3, \"RowTemplate\", {fragmentOfInt}(default));", generated);
        Assert.Contains(
            $"__builder.AddComponentParameter(5, \"RowTemplate\", {fragmentOfInt}"
                + "(default(global::Microsoft.AspNetCore.Components.RenderFragment<int>)));",
            generated);
        Assert.Contains(
            $"__builder.AddComponentParameter(7, \"RowTemplate\", {fragmentOfInt}"
                + "((global::Microsoft.AspNetCore.Components.RenderFragment<int>?)null));",
            generated);
        Assert.Contains(
            $"__builder.AddComponentParameter(9, \"RowTemplate\", {fragmentOfInt}(_fragment));",
            generated);
    }

    private static void AssertGeneratedContextReferencesBindOuterParameter(
        GeneratorRunResult result,
        string contextName)
    {
        var generated = result.GeneratedSources.Single(s => s.HintName.Contains("Host"));
        var tree = generated.SyntaxTree;
        var semanticModel = result.OutputCompilation.GetSemanticModel(tree);
        var contextualLambda = tree.GetRoot().DescendantNodes()
            .OfType<ParenthesizedLambdaExpressionSyntax>()
            .Single(lambda =>
                lambda.ParameterList.Parameters is [{ Identifier.ValueText: var name }]
                && name == contextName);
        var contextSymbol = semanticModel.GetDeclaredSymbol(
            contextualLambda.ParameterList.Parameters[0])!;
        var references = contextualLambda.Body.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => identifier.Identifier.ValueText == contextName)
            .ToArray();

        Assert.NotEmpty(references);
        Assert.All(
            references,
            reference => Assert.True(
                SymbolEqualityComparer.Default.Equals(
                    contextSymbol,
                    semanticModel.GetSymbolInfo(reference).Symbol),
                $"Generated reference '{reference}' did not bind to the outer contextual parameter."));
    }
}
